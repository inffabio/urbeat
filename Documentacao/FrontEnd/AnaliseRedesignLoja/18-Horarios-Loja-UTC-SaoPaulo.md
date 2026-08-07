# Horários da loja: UTC/São Paulo

## Regra implementada

- O backend é a fonte única de verdade para `isOpenNow`.
- A hora atual entra como UTC e é convertida para o fuso `America/Sao_Paulo` antes de comparar `DayOfWeek`, `StartTime` e `EndTime`.
- Em Windows, o fallback do fuso é `E. South America Standard Time`.
- O frontend não calcula horário comercial: ele consome `isOpenNow`, `closedMessage` e `nextStatusChangeAt` do backend.

## Transição automática

- Quando a loja está aberta, `nextStatusChangeAt` aponta para o próximo fechamento.
- Quando a loja está fechada por horário, `nextStatusChangeAt` aponta para a próxima abertura.
- Cardápio e carrinho agendam um novo GET nesse timestamp para refletir automaticamente abertura ou fechamento.
- Ao tentar abrir um produto com a loja fechada, o cardápio exibe a mensagem enviada pelo backend, por exemplo: `A loja só estará aberta Terça às 18:00.`

## Observações

- `Store.IsOpen` continua funcionando como estado manual/administrativo da loja.
- Se `Store.IsOpen` estiver falso, a loja fica fechada independentemente dos turnos configurados.
- Turnos que cruzam meia-noite são suportados.
