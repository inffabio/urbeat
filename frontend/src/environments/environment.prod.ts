// Em produção, o frontend é servido pelo mesmo nginx que faz proxy /api/
// para a WebAPI. apiUrl vazio => chamadas relativas (mesma origem).
export const environment = {
  production: true,
  apiUrl: '',
  signalRBase: '',
  useMock: false,
};
