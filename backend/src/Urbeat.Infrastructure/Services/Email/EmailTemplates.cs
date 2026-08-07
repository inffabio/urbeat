namespace Urbeat.Infrastructure.Services.Email;

internal static class EmailTemplates
{
    public static (string Subject, string HtmlBody) BuildCustomerConfirmation(string confirmUrl)
    {
        const string subject = "Confirme seu e-mail no Urbeat";
        var html = BaseLayout(
            title: "Bem-vindo(a) ao Urbeat!",
            greeting: "Olá!",
            body: """
                <p>Estamos quase lá. Para começar a fazer pedidos no <strong>Urbeat</strong>,
                precisamos confirmar seu e-mail.</p>
                <p>Clique no botão abaixo para confirmar sua conta. O link expira em 24 horas.</p>
                """,
            buttonText: "Confirmar meu e-mail",
            buttonUrl: confirmUrl,
            footer: """
                <p>Se você não criou uma conta no Urbeat, pode ignorar este e-mail
                com tranquilidade.</p>
                """);

        return (subject, html);
    }

    public static (string Subject, string HtmlBody) BuildSellerConfirmation(string confirmUrl)
    {
        const string subject = "Confirme seu e-mail no Urbeat — Loja";
        var html = BaseLayout(
            title: "Sua loja está quase pronta!",
            greeting: "Olá, vendedor(a)!",
            body: """
                <p>Recebemos o cadastro da sua loja no <strong>Urbeat</strong>.
                Para liberar o painel de vendas e receber pedidos, confirme seu e-mail.</p>
                <p>Clique no botão abaixo para ativar sua conta. O link expira em 24 horas.</p>
                """,
            buttonText: "Confirmar e-mail da loja",
            buttonUrl: confirmUrl,
            footer: """
                <p>Se você não criou um cadastro de loja no Urbeat, pode ignorar este e-mail.</p>
                """);

        return (subject, html);
    }

    public static (string Subject, string HtmlBody) BuildPasswordReset(string userName, string resetLink)
    {
        const string subject = "Recuperação de senha - Urbeat";
        var html = BaseLayout(
            title: "Recuperação de senha",
            greeting: $"Olá, {userName}!",
            body: """
                <p>Recebemos uma solicitação para redefinir a senha da sua conta no <strong>Urbeat</strong>.</p>
                <p>Clique no botão abaixo para criar uma nova senha. Este link expira em 1 hora.</p>
                """,
            buttonText: "Redefinir minha senha",
            buttonUrl: resetLink,
            footer: """
                <p>Se você não solicitou a recuperação de senha, ignore este e-mail.
                Sua senha permanecerá a mesma.</p>
                """);

        return (subject, html);
    }

    private static string BaseLayout(string title, string greeting, string body, string buttonText, string buttonUrl, string footer)
    {
        return $$"""
            <!DOCTYPE html>
            <html lang="pt-BR">
            <head>
              <meta charset="UTF-8" />
              <meta name="viewport" content="width=device-width,initial-scale=1.0" />
              <title>{{title}}</title>
            </head>
            <body style="margin:0;padding:0;background:#FFF9F0;font-family:'Helvetica Neue',Helvetica,Arial,sans-serif;color:#2D2A2A;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#FFF9F0;padding:32px 0;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="background:#FFFCF8;border:1px solid #EDE4D9;border-radius:20px;overflow:hidden;">
                      <tr>
                        <td style="background:#C73E3A;padding:28px 32px;color:#FFFCF8;">
                          <h1 style="margin:0;font-family:Georgia,serif;font-weight:400;font-size:28px;">Urbeat</h1>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px 40px 8px 40px;">
                          <h2 style="margin:0 0 8px 0;font-family:Georgia,serif;font-weight:400;color:#2D2A2A;font-size:24px;">{{title}}</h2>
                          <p style="margin:0;color:#7A7272;font-size:15px;">{{greeting}}</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:16px 40px;color:#2D2A2A;font-size:15px;line-height:1.6;">
                          {{body}}
                        </td>
                      </tr>
                      <tr>
                        <td align="center" style="padding:24px 40px 8px 40px;">
                          <a href="{{buttonUrl}}"
                             style="display:inline-block;background:#C73E3A;color:#FFFCF8;text-decoration:none;padding:14px 28px;border-radius:12px;font-weight:600;font-size:15px;">
                            {{buttonText}}
                          </a>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:8px 40px 24px 40px;color:#7A7272;font-size:13px;line-height:1.5;">
                          <p>Se o botão não funcionar, copie e cole o link abaixo no seu navegador:</p>
                          <p style="word-break:break-all;color:#C73E3A;"><a href="{{buttonUrl}}" style="color:#C73E3A;">{{buttonUrl}}</a></p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:16px 40px 32px 40px;border-top:1px solid #EDE4D9;color:#7A7272;font-size:13px;line-height:1.5;">
                          {{footer}}
                          <p style="margin-top:16px;">— Equipe Urbeat</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
