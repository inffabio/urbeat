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
};
