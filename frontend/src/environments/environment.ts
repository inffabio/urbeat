// Dev: chamadas relativas vão para o backend real via proxy do `ng serve`
// (configurar `proxy.conf.json` se rodar frontend localmente apontando para
// http://localhost:5000 do webapi).
export const environment = {
  production: false,
  apiUrl: '',
  signalRBase: '',
  useMock: false,
};
