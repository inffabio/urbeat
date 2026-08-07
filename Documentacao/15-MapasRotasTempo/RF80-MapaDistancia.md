
# Cálculo de Distância e Tempo de Entrega com Mapbox

## Criação de algoritimo usando o Mapbox para calcular o tempo entre restaurante localizacao do endereco de entrega

## Regra de Negocio
>
> O sistema deve calcular pelo MapBox a distancia entre o estabelecimento e o Endereco do cliente. Todos enderecos tem longitude e latitude no registro.  Só mostrar na vitrine restaurantes com até 5 km de distância. Preparar um algoritimo para calcular o tempo de entrega de moto/bike. Criando assim endpoints que geram estas informações quando solicitadas pelo cliente.

- A chave de acesso do MapBox do cliente Urbeat está no arquivo chave do [.././InstrucoesFrontEnd.md]

## Checklist Técnico de Implementação (Pendente)
- [ ] No Backend, nenhuma integração via API REST ou EF Core GeoSpatial foi adicionada ainda.
- [ ] Para a filtragem de `< 5km`, requer que as tabelas de endereço de loja e de cliente tenham Lat e Lng. (Ou então, que a limitação ocorra por geocoding sob demanda).
- [ ] Atualizar o método `ListPublicAsync` no `PublicStoresController` para que a filtragem por distância ocorra antes do `Ok(stores)`. Pode requer NetTopologySuite com PostGIS/SQLServer Spatial.
