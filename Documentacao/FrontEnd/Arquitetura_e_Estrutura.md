# Documentação da Arquitetura e Estrutura do Front-End (Urbeat)

Esta documentação fornece uma visão global do front-end do projeto **Urbeat**. Ele encapsula a estrutura de arquivos, as principais convenções adotadas e a organização modular do aplicativo.

## 1. Visão Geral e Tecnologias

O projeto front-end foi construído visando performance, modularização e boa experiência de usuário (UX).

* **Framework:** Angular (v20)
* **Linguagem:** TypeScript
* **Estilização:** SCSS global e por componente.
* **UI Components:** Ionic Framework (utilizado para botões, ícones `<ion-icon>` e estrutura de view `<ion-content>`).
* **Roteamento:** Configurado com `app.routes.ts` com base no sistema de roteamento do Angular.
* **Tratamento de Assets:** Os assets estáticos mudaram de referência relativa (`assets/images/`) para requisições na raiz absoluta (`/images/`), para adequar-se à construção moderna do Angular (`public/`).

---

## 2. Resumo de Funcionalidades Implementadas / Tratadas

* **Onboarding do Vendedor (`/features/seller-register`)**: Componente que abstrai as regras de segurança rígidas vindas do ASP.NET Identity (como complexidade de layout de senhas). Conta com tratamento de erro em Português-BR extraído via Interceptor do pipeline.
* **Configuração e Publicação da Loja (`/features/store-config`)**: Foram criadas etapas como configuração de Horários, Entregas (Raio e Valores) e, finalmente, a subpágina de "Publicar", que unifica a visão geral da loja que autoriza sua ida para o feed principal da plataforma.
* **Fix de Diretivas de Imagem:** A nova estratégia adota referências de base absolutas (`src="/images/logo_v2.png"`) sobre os ícones e logotipos em todas as páginas para evitar quebras visuais em tempo de deploy (garantindo referenciamento correto no diretório `public/` exposto pelo compilador Angular atual).

## 3. Compilação e Deploy

O aplicativo passa pelos seguintes estágios de compilação:

1. **Build Local/Contínua:** `npm run build` cria um bundle Otimizado na pasta `/dist/frontend/` suportando "Lazy loading" - quebrando os componentes de página em pequenos _chunks_ (ex: `store-config-page-component` com tamanho enxuto) para melhor performance do cliente.
2. **Implantação via Script (Kamatera):** É executado o PowerScript `\scripts\deploy-kamatera.ps1` no backend para parear e efetuar o `Docker Build` / `Docker Compose` na VPS (`52.144.45.199`), subindo um NGINX encarregado de injetar esse estático de `/dist` ao cliente consumidor.