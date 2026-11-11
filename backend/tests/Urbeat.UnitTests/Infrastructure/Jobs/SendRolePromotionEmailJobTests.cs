using System.Text.RegularExpressions;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Urbeat.Application.Interfaces;
using Urbeat.Domain.Entities;
using Urbeat.Infrastructure.Identity;
using Urbeat.Infrastructure.Jobs;
using Urbeat.Infrastructure.Persistence;
using Urbeat.Infrastructure.Services.Email;

namespace Urbeat.UnitTests.Infrastructure.Jobs;

/// <summary>
/// The promotion e-mail must be deliverable from the persisted request alone: neither Redis nor the
/// original Hangfire enqueue is required, so a late job or a cache loss cannot lose the link.
/// </summary>
public sealed class SendRolePromotionEmailJobTests
{
    private const string SigningKey = "urbeat-test-role-promotion-signing-key-32chars";
    private static readonly TimeSpan Ttl = RolePromotionChallengeService.ChallengeTtl;

    [Fact]
    public async Task ExecuteAsync_ShouldBuildTheLinkFromTheRequest_WithoutAnyCacheEntry()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var challenge = CreateChallengeService(db, time);
        var created = await challenge.CreateAsync(BuildData());
        var emailService = new RecordingEmailService();

        var job = CreateJob(challenge, emailService);
        await job.ExecuteAsync(created.RequestId);

        emailService.Messages.Should().ContainSingle();
        var sent = emailService.Messages[0];
        sent.ToAddress.Should().Be("cliente@urbeat.local");
        sent.Subject.Should().Be("Confirme seu novo acesso de lojista no urbeat");
        sent.HtmlBody.Should().Contain("não altera sua senha atual");
        sent.HtmlBody.Should().Contain("30 minutos");
        sent.HtmlBody.Should().Contain("#promotionToken=");
        sent.HtmlBody.Should().NotContain("?promotionToken=");
        sent.IdempotencyKey.Should().Be($"role-promotion-{created.RequestId:N}");
        AssertIsValidMessageIdLocalPart(sent.IdempotencyKey!);

        var token = ExtractPromotionToken(sent.HtmlBody);
        token.Should().Be(created.Token);

        // The delivered link is the real challenge, not a placeholder.
        (await challenge.ValidateAndConsumeAsync(token)).IsValid.Should().BeTrue();

        (await challenge.FindAsync(created.RequestId))!.EmailSentAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStillSendWhenTheJobRunsLateWithinTheTtl()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var challenge = CreateChallengeService(db, time);
        var created = await challenge.CreateAsync(BuildData());
        var emailService = new RecordingEmailService();

        // The job was delayed: 29 of the 30 minutes are gone, but the request is still valid.
        time.UtcNow = time.UtcNow.Add(Ttl).AddMinutes(-1);

        await CreateJob(challenge, emailService).ExecuteAsync(created.RequestId);

        emailService.Messages.Should().ContainSingle();
        var token = ExtractPromotionToken(emailService.Messages[0].HtmlBody);
        (await challenge.ValidateAndConsumeAsync(token)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSkipExpiredRequests()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var challenge = CreateChallengeService(db, time);
        var created = await challenge.CreateAsync(BuildData());
        var emailService = new RecordingEmailService();

        time.UtcNow = time.UtcNow.Add(Ttl).AddSeconds(1);

        await CreateJob(challenge, emailService).ExecuteAsync(created.RequestId);

        emailService.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotSendTwiceForTheSameRequest()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var challenge = CreateChallengeService(db, time);
        var created = await challenge.CreateAsync(BuildData());
        var emailService = new RecordingEmailService();
        var job = CreateJob(challenge, emailService);

        await job.ExecuteAsync(created.RequestId);
        await job.ExecuteAsync(created.RequestId);

        emailService.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotSendWhileAnotherJobHoldsTheDeliveryLease()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var challenge = CreateChallengeService(db, time);
        var created = await challenge.CreateAsync(BuildData());

        // Another worker already claimed the delivery (duplicate enqueue or concurrent recovery).
        var held = await challenge.TryClaimEmailDeliveryAsync(created.RequestId);
        held.Should().NotBeNull();

        var emailService = new RecordingEmailService();
        await CreateJob(challenge, emailService).ExecuteAsync(created.RequestId);

        emailService.Messages.Should().BeEmpty("a valid lease must block a concurrent/late job");
        (await challenge.FindAsync(created.RequestId))!.EmailSentAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReuseTheSameDeterministicMessageIdOnRetry()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var challenge = CreateChallengeService(db, time);
        var created = await challenge.CreateAsync(BuildData());
        var emailService = new RecordingEmailService();
        var job = CreateJob(challenge, emailService);

        await job.ExecuteAsync(created.RequestId);

        // Simulate a crash after SMTP accepted the message but before EmailSentAtUtc was persisted:
        // the lease is gone/expired and recovery retries the same request.
        var request = await db.RolePromotionRequests.SingleAsync();
        request.EmailSentAtUtc = null;
        request.EmailSendingAtUtc = null;
        request.EmailSendAttemptId = null;
        await db.SaveChangesAsync();

        await job.ExecuteAsync(created.RequestId);

        emailService.Messages.Should().HaveCount(2);
        var messageId = emailService.Messages
            .Select(message => message.IdempotencyKey)
            .Distinct()
            .Should()
            .ContainSingle()
            .Which;
        messageId.Should().Be(SendRolePromotionEmailJob.CreateMessageIdempotencyKey(created.RequestId));
        messageId.Should().Be(SendRolePromotionEmailJob.CreateMessageIdempotencyKey(created.RequestId));
        AssertIsValidMessageIdLocalPart(messageId!);
    }

    [Fact]
    public async Task RecoveryJob_ShouldReenqueuePendingRequestsThatWereNeverSent()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var challenge = CreateChallengeService(db, time);
        var stranded = await challenge.CreateAsync(BuildData());
        var delivered = await challenge.CreateAsync(BuildData(UserRoleType.Customer));
        var deliveredClaim = await challenge.TryClaimEmailDeliveryAsync(delivered.RequestId);
        deliveredClaim.Should().NotBeNull();
        await challenge.MarkEmailSentAsync(delivered.RequestId, deliveredClaim!.AttemptId);

        // Only requests older than the grace period are considered stranded.
        time.UtcNow = time.UtcNow.AddMinutes(2);

        var jobClient = new RecordingBackgroundJobClient();
        var recovery = new RolePromotionEmailRecoveryJob(
            challenge,
            jobClient,
            NullLogger<RolePromotionEmailRecoveryJob>.Instance);

        await recovery.ExecuteAsync();

        jobClient.Enqueued.Should().ContainSingle();
        jobClient.Enqueued[0].Type.Should().Be(typeof(SendRolePromotionEmailJob));
        jobClient.Enqueued[0].Args.Should().ContainSingle().Which.Should().Be(stranded.RequestId);
    }

    [Fact]
    public async Task RecoveryJob_ShouldNotReenqueueRequestsInsideTheGracePeriod()
    {
        using var db = CreateDbContext();
        var time = new TestTimeProvider();
        var challenge = CreateChallengeService(db, time);
        await challenge.CreateAsync(BuildData());

        var jobClient = new RecordingBackgroundJobClient();
        var recovery = new RolePromotionEmailRecoveryJob(
            challenge,
            jobClient,
            NullLogger<RolePromotionEmailRecoveryJob>.Instance);

        await recovery.ExecuteAsync();

        jobClient.Enqueued.Should().BeEmpty();
    }

    private static SendRolePromotionEmailJob CreateJob(
        IRolePromotionChallengeService challenge,
        IEmailService emailService)
        => new(
            challenge,
            emailService,
            Options.Create(new EmailConfirmationOptions
            {
                FrontendBaseUrl = "https://app.urbeat.test",
                RolePromotionPath = "/confirmacao-email"
            }),
            NullLogger<SendRolePromotionEmailJob>.Instance);

    private static RolePromotionChallengeService CreateChallengeService(
        ApplicationDbContext db,
        TimeProvider time)
        => new(
            db,
            Options.Create(new RolePromotionChallengeOptions { SigningKey = SigningKey }),
            time,
            NullLogger<RolePromotionChallengeService>.Instance);

    private static RolePromotionRequestData BuildData(UserRoleType role = UserRoleType.Seller) => new()
    {
        UserId = Guid.CreateVersion7(),
        Email = "cliente@urbeat.local",
        TargetRole = role,
        PasswordHash = "AQAAAAIAAYagAAAAEHASHVALUE",
        FullName = "Contratante Teste"
    };

    private static string ExtractPromotionToken(string html)
    {
        var match = Regex.Match(html, "#promotionToken=(?<token>[a-zA-Z0-9_-]+)");
        match.Success.Should().BeTrue("the promotion e-mail must contain the derived confirmation token");
        return match.Groups["token"].Value;
    }

    /// <summary>
    /// The idempotency key becomes the local part of the outbound Message-Id
    /// (<c>&lt;local@urbeat&gt;</c>), so it must be a valid RFC 5322 dot-atom: no colon, whitespace
    /// or other specials.
    /// </summary>
    private static void AssertIsValidMessageIdLocalPart(string value)
    {
        value.Should().MatchRegex("^[A-Za-z0-9_~-]+(?:\\.[A-Za-z0-9_~-]+)*$");
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"urbeat-role-promotion-job-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed record RecordedEmail(
        string ToAddress,
        string Subject,
        string HtmlBody,
        string? IdempotencyKey);

    private sealed class RecordingEmailService : IEmailService
    {
        public List<RecordedEmail> Messages { get; } = [];

        public Task<string?> SendAsync(
            string toAddress,
            string toName,
            string subject,
            string htmlBody,
            string? textBody = null,
            string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            Messages.Add(new RecordedEmail(toAddress, subject, htmlBody, idempotencyKey));
            return Task.FromResult<string?>(idempotencyKey);
        }
    }

    private sealed class RecordingBackgroundJobClient : IBackgroundJobClient
    {
        public List<Job> Enqueued { get; } = [];

        public string? Create(Job job, IState state)
        {
            Enqueued.Add(job);
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string? expectedState) => true;
    }
}
