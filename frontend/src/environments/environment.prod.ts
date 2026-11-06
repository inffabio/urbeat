// Em produção, o frontend é servido pelo mesmo nginx que faz proxy /api/
// para a WebAPI. apiUrl vazio => chamadas relativas (mesma origem).
export const environment = {
  production: true,
  apiUrl: '',
  signalRBase: '',
  useMock: false,
  // MVP: endpoint público de demonstração do OSRM. Ele não tem SLA e aplica limites de
  // uso; produção deve trocar por serviço de roteamento próprio (ou proxy com cota e
  // observabilidade). A troca é feita só aqui, sem alterar o componente do mapa.
  routingUrl: 'https://router.project-osrm.org/route/v1/driving',
  // Meta/Facebook JS SDK (browser). O App ID é público e pode ser versionado; deixe
  // `metaAppId` vazio para desabilitar o SDK (nenhum script é carregado). Segredos do
  // WhatsApp/Meta (tokens, app secret) ficam somente no backend e nunca aqui.
  metaAppId: '',
  // Versão da Graph API enviada ao `FB.init` (ex.: 'v26.0'). Vazio usa o default
  // fixado em `facebook-sdk.loader.ts`; mantenha em sincronia com o painel da Meta.
  metaApiVersion: '',
};
