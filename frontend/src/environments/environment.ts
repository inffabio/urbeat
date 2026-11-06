// Dev: chamadas relativas vão para o backend real via proxy do `ng serve`
// (configurar `proxy.conf.json` se rodar frontend localmente apontando para
// http://localhost:5000 do webapi).
export const environment = {
  production: false,
  apiUrl: '',
  signalRBase: '',
  useMock: false,
  // Roteamento por ruas do mapa do entregador. O endpoint público do OSRM é apenas o
  // MVP (demo, sem SLA e com limite de uso). Aponte para um serviço próprio/proxy com
  // cota adequada antes de aumentar o volume. Nenhuma API key é embutida no browser.
  routingUrl: 'https://router.project-osrm.org/route/v1/driving',
  // Meta/Facebook JS SDK (browser). O App ID é público e pode ser versionado; deixe
  // `metaAppId` vazio para desabilitar o SDK (nenhum script é carregado). Segredos do
  // WhatsApp/Meta (tokens, app secret) ficam somente no backend e nunca aqui.
  metaAppId: '',
  // Versão da Graph API enviada ao `FB.init` (ex.: 'v26.0'). Vazio usa o default
  // fixado em `facebook-sdk.loader.ts`; mantenha em sincronia com o painel da Meta.
  metaApiVersion: '',
};
