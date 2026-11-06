# Meta / Facebook JS SDK no frontend

Integração inicial e **opcional** do SDK JavaScript da Meta no browser do urbeat.

## Escopo

- O SDK do Facebook (`connect.facebook.net/.../sdk.js`) serve apenas a recursos Meta
  no navegador (login, plugins sociais, `AppEvents`).
- **Não** é canal de envio de WhatsApp. A WhatsApp Business Cloud API é uma integração
  **server-side** e deve manter token/app secret somente no backend. Nada disso existe
  neste escopo e nenhum segredo é lido ou embutido no frontend.

## Configuração

Arquivos: `frontend/src/environments/environment.ts` (dev) e
`frontend/src/environments/environment.prod.ts` (produção).

```ts
metaAppId: '',        // App ID público da Meta; vazio desabilita o SDK
metaApiVersion: '',   // ex.: 'v26.0'; vazio usa o default fixado no loader
```

- `metaAppId` é **público** (vai para o bundle) e pode ser versionado.
- Nunca coloque `app secret`, token do WhatsApp, token de página ou qualquer credencial
  nesses arquivos. Segredos pertencem ao backend/variáveis de ambiente do servidor.
- Encontre o App ID em `developers.facebook.com` → seu app → Configurações → Básico.
- Mantenha `metaApiVersion` em sincronia com a versão habilitada no painel da Meta.
  Sem valor, o loader usa `DEFAULT_FACEBOOK_API_VERSION` (`v26.0`).

Com `metaAppId` vazio, `FacebookSdkService.load()` resolve `null` e **nenhum script é
inserido** no documento.

## Uso

O SDK é **opt-in**: nada é carregado no boot da aplicação. O projeto ainda não possui
banner/mecanismo de consentimento de cookies, então um componente só deve chamar o
serviço depois de uma ação/consentimento explícito do usuário. Não adicione um banner
nesta integração sem uma decisão de produto/LGPD.

```ts
import { inject } from '@angular/core';
import { FacebookSdkService } from '@core/services/facebook-sdk.service';

const facebook = inject(FacebookSdkService);

if (facebook.isConfigured && userConsentedToMeta) {
  await facebook.load(); // null quando desabilitado, bloqueado ou com timeout
}
```

O loader:

- injeta o script uma única vez (`id="facebook-jssdk"`) e compartilha a promise entre
  chamadas concorrentes;
- inicializa via `window.fbAsyncInit` com `{ appId, cookie: true, xfbml: true, version }`;
- chama `FB.AppEvents.logPageView()` apenas quando a API existe;
- resolve `null` (nunca rejeita) em timeout, erro de script, `FB.init` inválido ou
  ambiente sem `document` (SSR), sem bloquear o boot;
- não cacheia falhas, permitindo nova tentativa na mesma sessão.

## Status de login (`FB.getLoginStatus`)

`FacebookSdkService.getLoginStatus()` expõe o estado da sessão Meta **sem** autenticar
ninguém no urbeat. Ele carrega o SDK (`load()`), chama `FB.getLoginStatus` **uma única
vez** e devolve a resposta normalizada ou `null`.

```ts
import { inject } from '@angular/core';
import { FacebookSdkService } from '@core/services/facebook-sdk.service';

const facebook = inject(FacebookSdkService);

const status = await facebook.getLoginStatus(); // null = indisponível/desconhecido

if (status?.status === 'connected') {
  // Existe um token da Meta no browser — ele NÃO é uma sessão urbeat.
  // Este serviço não persiste nem envia esse token para o backend.
  // Qualquer login real deve validar `authResponse.signedRequest`/`accessToken`
  // no servidor antes de emitir um JWT do urbeat.
}
```

Tipos (`facebook-sdk.types.ts`):

```ts
type FacebookLoginStatus = 'connected' | 'not_authorized' | 'unknown';

interface FacebookLoginStatusResponse {
  status: FacebookLoginStatus;
  authResponse?: {
    accessToken: string;
    userID: string;
    expiresIn: number;
    signedRequest?: string;
    grantedScopes?: string;
    graphDomain?: string;
    data_access_expiration_time?: number;
  } | null;
}
```

Comportamento do método:

- resolve `null` quando `metaAppId` está vazio, quando o SDK não carrega (timeout/erro
  de script) ou quando a build não expõe `FB.getLoginStatus`;
- aceita `{ scope, forceRoundTrip, timeoutMs }`, repassados como segundo argumento do
  SDK; sem opções, o SDK é chamado apenas com o callback;
- trata callback, exceção síncrona, payload inválido e timeout (`FACEBOOK_LOGIN_STATUS_TIMEOUT_MS`,
  10s) resolvendo `null` — nunca rejeita, então não quebra o fluxo chamador;
- considera `connected` sem `authResponse` utilizável como `null`;
- **não** chama `FB.login`/pop-up, **não** grava token em `localStorage`/`sessionStorage`
  e **não** usa `AuthService`.

### Ressalvas de produto

- `connected` significa apenas "o navegador tem uma sessão Meta para este App ID". O
  login próprio do urbeat (`AuthService`, JWT, perfis de cliente/lojista/admin) continua
  sendo o único caminho de autenticação.
- Para transformar isso em "Entrar com Facebook" de verdade é preciso, no painel da
  Meta, adicionar e configurar o produto **Facebook Login** (redirect/domínios válidos,
  escopos aprovados) e implementar no **backend** a troca/validação server-side do
  `accessToken` ou `signedRequest` antes de criar/associar a conta. Isso está fora
  deste escopo e não foi implementado.
- Login de lojista/admin não é, e não deve ser, autenticado por esse caminho.
- Para **WhatsApp Cloud API** continuam sendo necessários credenciais e backend
  próprios (token, Phone Number ID, verificação de webhook). O SDK do browser não
  envia mensagens nem substitui essa integração.

## Testes

```bash
cd frontend
npx jest --no-coverage src/app/core/services/facebook-sdk.loader.spec.ts
npx jest --no-coverage src/app/core/services/facebook-sdk.service.spec.ts
```

## Limitações

- Sem mecanismo de consentimento implementado: a integração permanece desligada por
  configuração vazia e sem chamadas automáticas.
- Nenhum componente chama `getLoginStatus()` no boot e não existe botão "Entrar com
  Facebook"; o método só roda sob demanda explícita.
- Sem CSP específica no nginx atual; se uma CSP for adicionada, `connect.facebook.net`
  precisa constar em `script-src` (e domínios da Meta em `connect-src`/`frame-src`).
- Nenhuma dependência npm foi adicionada; o SDK é carregado em runtime.
