# Backend Security And Payment Hardening Design

## Goal

Corrigir os achados de segurança, autorização, pagamento e operação identificados na revisão do backend sem quebrar o onboarding de confirmação de e-mail.

## Decisions

- A troca de e-mail antes da confirmação usará um desafio temporário, assinado e de uso único, emitido junto ao cadastro; o endpoint não aceitará `UserId` como autoridade.
- Mutations de tempos de entrega e bairros serão vinculadas ao proprietário autenticado da loja. Operações globais de bairro serão restritas a autorização administrativa quando aplicável.
- O checkout do Mercado Pago receberá o frete como item/valor autorizado e o retorno do gateway será validado contra o total persistido antes da transição de pagamento.
- Reset de senha revogará refresh tokens existentes da conta.
- `forgot-password` terá resposta indistinguível para e-mail existente/inexistente e limite de requisições.
- CORS aceitará somente origens configuradas explicitamente.

## Compatibility

- O fluxo `/confirmacao-email` continuará permitindo troca de endereço antes da confirmação, agora usando o desafio temporário.
- Clientes existentes com links antigos não poderão alterar e-mail sem um desafio válido; poderão reiniciar o cadastro/fluxo de recuperação.
- Preços, frete e transições continuam sob autoridade do backend.

## Verification

- Testes unitários cobrirão desafio de e-mail, revogação de sessão, rate limiting e autorização cross-store.
- Testes de checkout cobrirão frete incluído e divergência de valor do gateway.
- A suíte unitária e de integração do backend, build da solução e validação de CORS serão executados antes do commit.
