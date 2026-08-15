namespace Urbeat.Infrastructure.Services.Email;

internal static class EmailTemplates
{
    public static (string Subject, string HtmlBody) BuildCustomerConfirmation(string confirmUrl)
    {
        const string subject = "Confirme seu e-mail no UrBeat";
        var html = BaseLayout(
            preheader: "Falta um clique para ativar sua conta.",
            title: "Bem-vindo(a) ao UrBeat!",
            greeting: "Olá!",
            body: """
                <p style="margin:0 0 16px 0;">Recebemos seu cadastro e estamos quase lá. Para começar a fazer pedidos, precisamos confirmar seu e-mail.</p>
                <p style="margin:0;">Clique no botão abaixo para ativar sua conta.</p>
                """,
            buttonText: "Confirmar meu e-mail",
            buttonUrl: confirmUrl,
            notice: "Por segurança, o link de confirmação expira em <strong>24 horas</strong>.",
            footer: """
                <p style="margin:0 0 8px 0;">Se você não criou uma conta no UrBeat, pode ignorar este e-mail com tranquilidade.</p>
                """);

        return (subject, html);
    }

    public static (string Subject, string HtmlBody) BuildSellerConfirmation(string confirmUrl)
    {
        const string subject = "Confirme seu e-mail no UrBeat — sua loja está quase pronta";
        var html = BaseLayout(
            preheader: "Falta um clique para liberar o painel da sua loja.",
            title: "Sua loja está quase pronta!",
            greeting: "Olá, vendedor(a)!",
            body: """
                <p style="margin:0 0 16px 0;">Recebemos o cadastro da sua loja no <strong>UrBeat</strong>. Para liberar o painel de vendas e começar a receber pedidos, confirme seu e-mail.</p>
                <p style="margin:0;">Clique no botão abaixo para ativar sua conta.</p>
                """,
            buttonText: "Confirmar e-mail da loja",
            buttonUrl: confirmUrl,
            notice: "Por segurança, o link de confirmação expira em <strong>24 horas</strong>.",
            footer: """
                <p style="margin:0 0 8px 0;">Se você não criou um cadastro de loja no UrBeat, pode ignorar este e-mail.</p>
                """);

        return (subject, html);
    }

    public static (string Subject, string HtmlBody) BuildPasswordReset(string userName, string resetLink)
    {
        const string subject = "Recuperação de senha - UrBeat";
        var html = BaseLayout(
            preheader: "Crie uma nova senha para sua conta.",
            title: "Recuperação de senha",
            greeting: $"Olá, {userName}!",
            body: """
                <p style="margin:0 0 16px 0;">Recebemos uma solicitação para redefinir a senha da sua conta no <strong>UrBeat</strong>.</p>
                <p style="margin:0;">Clique no botão abaixo para criar uma nova senha.</p>
                """,
            buttonText: "Redefinir minha senha",
            buttonUrl: resetLink,
            notice: "Por segurança, o link de redefinição expira em <strong>1 hora</strong>.",
            footer: """
                <p style="margin:0 0 8px 0;">Se você não solicitou a recuperação de senha, ignore este e-mail. Sua senha permanecerá a mesma.</p>
                """);

        return (subject, html);
    }

    private static string BaseLayout(
        string preheader,
        string title,
        string greeting,
        string body,
        string buttonText,
        string buttonUrl,
        string notice,
        string footer)
    {
        return $$"""
            <!DOCTYPE html>
            <html lang="pt-BR">
            <head>
              <meta charset="UTF-8" />
              <meta name="viewport" content="width=device-width,initial-scale=1.0" />
              <meta name="x-apple-disable-message-reformatting" />
              <title>{{title}}</title>
              <!-- Preheader (pré-visualização do e-mail) -->
              <div style="display:none;max-height:0;overflow:hidden;mso-hide:all;">
                {{preheader}}&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;
              </div>
            </head>
            <body style="margin:0;padding:0;background:#F4F6F3;font-family:'Plus Jakarta Sans','Segoe UI',Helvetica,Arial,sans-serif;color:#3F3F46;-webkit-font-smoothing:antialiased;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#F4F6F3;padding:40px 16px;">
                <tr>
                  <td align="center">
                    <!-- ══ Cartão principal ══ -->
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:600px;width:100%;background:#FFFFFF;border:1px solid #E8EDE6;border-radius:20px;overflow:hidden;box-shadow:0 8px 28px rgba(20,30,20,0.06);">

                      <!-- ══ Cabeçalho ══ -->
                      <tr>
                        <td style="padding:32px 40px 0 40px;text-align:left;border-top:4px solid #6EAF4A;">
                          <img src="https://www.urbeat.com.br/assets/images/urbeat-logo.jpg" alt="UrBeat" width="132" style="height:auto;width:132px;display:block;border:0;" />
                        </td>
                      </tr>

                      <!-- ══ Título e saudação ══ -->
                      <tr>
                        <td style="padding:28px 40px 0 40px;">
                          <h1 style="margin:0 0 10px 0;font-size:26px;line-height:1.25;font-weight:800;color:#18181B;letter-spacing:-0.02em;">{{title}}</h1>
                          <p style="margin:0;font-size:15px;color:#71717A;">{{greeting}}</p>
                        </td>
                      </tr>

                      <!-- ══ Corpo ══ -->
                      <tr>
                        <td style="padding:20px 40px 0 40px;font-size:15px;line-height:1.65;color:#3F3F46;">
                          {{body}}
                        </td>
                      </tr>

                      <!-- ══ Botão CTA ══ -->
                      <tr>
                        <td align="left" style="padding:28px 40px 8px 40px;">
                          <a href="{{buttonUrl}}" style="display:inline-block;background:#6EAF4A;color:#FFFFFF;text-decoration:none;padding:15px 30px;border-radius:999px;font-weight:700;font-size:15px;letter-spacing:0.01em;">{{buttonText}} &rarr;</a>
                        </td>
                      </tr>

                      <!-- ══ Aviso (prazo do link) ══ -->
                      <tr>
                        <td style="padding:20px 40px 0 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#E8F5E9;border:1px solid rgba(110,175,74,0.25);border-radius:12px;">
                            <tr>
                              <td style="padding:12px 16px;font-size:13px;line-height:1.55;color:#2E7D32;">
                                <span style="font-weight:700;">&#9432;</span>&nbsp; {{notice}}
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>

                      <!-- ══ Link alternativo ══ -->
                      <tr>
                        <td style="padding:24px 40px 0 40px;color:#71717A;font-size:13px;line-height:1.55;">
                          <p style="margin:0 0 6px 0;">Se o botão não funcionar, copie e cole o link abaixo no seu navegador:</p>
                          <p style="margin:0;word-break:break-all;color:#2E7D32;"><a href="{{buttonUrl}}" style="color:#2E7D32;">{{buttonUrl}}</a></p>
                        </td>
                      </tr>

                      <!-- ══ Rodapé ══ -->
                      <tr>
                        <td style="padding:28px 40px 32px 40px;">
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                            <tr>
                              <td style="border-top:1px solid #E8EDE6;padding-top:20px;color:#71717A;font-size:13px;line-height:1.6;">
                                {{footer}}
                                <p style="margin:12px 0 0 0;font-weight:700;color:#18181B;">— Equipe UrBeat</p>
                                <p style="margin:8px 0 0 0;color:#A1A1AA;font-size:12px;">UrBeat &middot; Seu delivery, seu lucro.</p>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                    </table>

                    <!-- ══ Nota de rodapé externo ══ -->
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:600px;width:100%;">
                      <tr>
                        <td style="padding:18px 8px 0 8px;text-align:center;color:#A1A1AA;font-size:12px;line-height:1.5;">
                          &copy; {{DateTime.UtcNow.Year}} UrBeat &middot; Rio das Ostras, RJ, Brasil
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
