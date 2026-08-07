# Movimento em tempo real do entregador da origem até o destino

## Regra de negócio

> Criar uma API que devolva em tempo real o movimento do entregador da origem até o destino mostrando a motinho andando pelas ruas e o icone do ponto final aparecendo. Tudo isso consultando a api do MapBox.

- A chave de acesso do MapBox do cliente Urbeat está no arquivo chave do [../../InstrucoesFrontEnd.md]

## Checklist Técnico de Implementação (Pendente)
- [ ] Atualmente inexistes Hubs SignalR ou endpoints de rastreamento de entregador contínuo no Backend.
- [ ] Será necessário arquitetar um papel `Courier` (Entregador) no Sistema, ou permitir que o Seller dispare ping contínuo se for a sua própria frota.
- [ ] No Frontend (Angular), construir o componente de mapa utilizando a library base do Mapbox GL JS lendo um stream websocket que receberá `Latitude` e `Longitude`.