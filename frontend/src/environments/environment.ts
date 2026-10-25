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
};
