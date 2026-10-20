using FluentAssertions;
using Urbeat.PrintAgent.Models;
using Urbeat.PrintAgent.Storage;

namespace Urbeat.PrintAgent.Tests;

public class PrintJobStoreTests
{
    [Fact]
    public void Persisted_store_survives_restart_and_keeps_operation_key()
    {
        var path = Path.Combine(Path.GetTempPath(), $"urbeat-print-jobs-{Guid.NewGuid():N}.json");

        try
        {
            var first = new PrintJobStore(path);
            first.Add(new PrintJobRecord
            {
                Status = "sent",
                JobId = "42",
                PrinterName = "POS-58",
                ProfileId = "pos-58",
                Kind = "order",
                OperationKey = "order:1:accept:2026-10-19T12:00:00Z:printer:POS-58:profile:pos-58",
                CreatedAtUtc = "2026-10-19T12:00:01Z",
                Message = "sent"
            });

            var restarted = new PrintJobStore(path);
            var job = restarted.FindByOperationKey("order:1:accept:2026-10-19T12:00:00Z:printer:POS-58:profile:pos-58");

            job.Should().NotBeNull();
            job!.Status.Should().Be("sent");
            job.JobId.Should().Be("42");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Sending_job_is_recovered_as_ambiguous_after_restart_and_is_persisted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"urbeat-print-jobs-{Guid.NewGuid():N}.json");

        try
        {
            var first = new PrintJobStore(path);
            first.Add(new PrintJobRecord
            {
                Status = "sending",
                OperationKey = "op-ambiguous",
                PrinterName = "POS-58",
                ProfileId = "pos-58",
                Kind = "order",
                RawText = "PEDIDO 1",
                Message = "Enviando para o spooler."
            });

            var restarted = new PrintJobStore(path);
            var job = restarted.FindByOperationKey("op-ambiguous");

            job.Should().NotBeNull();
            job!.Status.Should().Be("ambiguous");
            job.Message.Should().Contain("resultado físico é desconhecido");

            var reloaded = new PrintJobStore(path).FindByOperationKey("op-ambiguous");
            reloaded!.Status.Should().Be("ambiguous");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Corrupt_store_starts_empty_instead_of_crashing_the_agent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"urbeat-print-jobs-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not valid json");

        try
        {
            var store = new PrintJobStore(path);

            store.List().Should().BeEmpty();
            store.LastLoadError.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            foreach (var candidate in Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}*"))
            {
                File.Delete(candidate);
            }
        }
    }

    [Fact]
    public void Update_replaces_the_existing_job_without_creating_a_duplicate()
    {
        var store = new PrintJobStore();
        var job = new PrintJobRecord
        {
            Status = "failed",
            OperationKey = "key-1",
            PrinterName = "POS-58"
        };
        store.Add(job);

        job.Status = "sent";
        job.JobId = "7";
        store.Update(job);

        var records = store.List();
        records.Should().ContainSingle();
        records[0].Status.Should().Be("sent");
    }
}
