# Dashboard Logo Storefront Link

## Objetivo

Permitir que o lojista abra a vitrine pública da própria loja diretamente pela
logo exibida no dashboard. Ao passar o mouse sobre a logo, o dashboard deve
exibir o texto `Clique para ir para loja`.

## Escopo

A alteração será feita no shell compartilhado do dashboard, que é responsável
por renderizar a logo. Ela abrangerá as três representações existentes:

- logo desktop;
- logo da sidebar recolhida;
- logo mobile.

## Design

Cada representação da logo será um link semântico para a rota pública baseada
no slug da loja:

```text
/${facade.store()?.slug}
```

O link abrirá a vitrine em uma nova aba usando `target="_blank"` e
`rel="noopener noreferrer"`. O elemento receberá `title="Clique para ir para
loja"` para o tooltip nativo e `aria-label="Clique para ir para loja"` para
acessibilidade.

As classes e estilos atuais serão preservados, incluindo o fallback visual
quando a loja não tiver logo. Nenhuma dependência ou componente de tooltip novo
será introduzido.

## Testes

Os testes do shell do dashboard serão ampliados para verificar que as variantes
da logo:

- apontam para o slug da loja;
- abrem em nova aba com a proteção de origem esperada;
- expõem o texto do tooltip e o nome acessível.

Também será verificado que o fallback visual existente continua renderizando
quando não há `logoUrl`.

## Critérios de aceite

1. Clicar na logo desktop abre `/<slug-da-loja>` em nova aba.
2. O mesmo comportamento existe na sidebar recolhida e no layout mobile.
3. O tooltip exibido ao passar o mouse informa exatamente `Clique para ir para loja`.
4. O link possui nome acessível equivalente e não introduz regressões visuais.
5. Os testes relevantes e o build do frontend passam.
