# 🎯 Proposta de dashboard do vendedor — Urbeat / Burger House

## ✅ O que montei
Criei uma proposta **visual e profissional** para a área do vendedor, aberta após a configuração da loja, com:

- 📌 **Sidebar lateral recolhível**
- 🧭 Ícones para:
  - **Horários**
  - **Entrega**
  - **Produtos**
  - **Visualizar publicação**
- 🧱 **Header com layout organizado**
  - **Esquerda:** logo **Urbeat**
  - **Centro:** nome da loja **Burger House**
  - **Direita:** logo da loja em formato redondo + ícone de pessoa com menu:
    - Alterar senha
    - Sair
- 📊 **Centro com gráficos redondos (gauges)**:
  - Faturamento diário
  - Faturamento mensal
  - Faturamento anual
  - Ticket médio
  - Entregas no prazo
  - Avaliação da loja
- 📈 Outros blocos visuais sugeridos:
  - Pedidos do dia
  - Tempo médio de entrega
  - Produtos mais vendidos
  - Bairros com mais pedidos
  - Status operacional

---

# 🧩 Estrutura de arquivos

```bash
dashboard-urbeat/
│
├── index.html
├── css/
│   └── styles.css
├── js/
│   └── app.js
└── assets/
    ├── logo-urbeat.svg
    ├── logo-loja-burger-house.svg
    └── icons/
        ├── horarios.svg
        ├── entrega.svg
        ├── produtos.svg
        ├── visualizar.svg
        ├── user.svg
        └── menu.svg
```

---

# 1) `index.html`

```html
<!DOCTYPE html>
<html lang="pt-BR">
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>Urbeat • Dashboard do Vendedor</title>
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800&display=swap" rel="stylesheet">
  <link rel="stylesheet" href="css/styles.css" />
</head>
<body>
  <div class="app-shell">
    <!-- Sidebar -->
    <aside class="sidebar" id="sidebar">
      <div class="sidebar-top">
        <button class="toggle-btn" id="toggleSidebar" aria-label="Abrir ou fechar menu">
          <img src="assets/icons/menu.svg" alt="Menu">
        </button>
      </div>

      <nav class="sidebar-nav">
        <a href="#" class="nav-item active">
          <img src="assets/icons/horarios.svg" alt="Horários">
          <span>Horários</span>
        </a>

        <a href="#" class="nav-item">
          <img src="assets/icons/entrega.svg" alt="Entrega">
          <span>Entrega</span>
        </a>

        <a href="#" class="nav-item">
          <img src="assets/icons/produtos.svg" alt="Produtos">
          <span>Produtos</span>
        </a>

        <a href="#" class="nav-item">
          <img src="assets/icons/visualizar.svg" alt="Visualizar publicação">
          <span>Visualizar publicação</span>
        </a>
      </nav>
    </aside>

    <!-- Main -->
    <div class="main-content">
      <!-- Header -->
      <header class="topbar">
        <div class="topbar-left">
          <img src="assets/logo-urbeat.svg" alt="Urbeat" class="urbeat-logo" />
        </div>

        <div class="topbar-center">
          <h1>Burger House</h1>
          <p>Hamburgueria • Painel do vendedor</p>
        </div>

        <div class="topbar-right">
          <div class="store-badge">
            <img src="assets/logo-loja-burger-house.svg" alt="Logo da loja" class="store-logo-round" />
          </div>

          <div class="profile-area">
            <button class="profile-btn" id="profileBtn">
              <img src="assets/icons/user.svg" alt="Usuário">
            </button>

            <div class="profile-menu" id="profileMenu">
              <a href="#">Alterar senha</a>
              <a href="#">Sair</a>
            </div>
          </div>
        </div>
      </header>

      <!-- Content -->
      <main class="dashboard">
        <section class="hero-panel">
          <div class="hero-copy">
            <span class="badge-online">● Loja aberta agora</span>
            <h2>Visão geral da operação</h2>
            <p>
              Acompanhe faturamento, pedidos, desempenho de entrega e produtos mais vendidos
              em uma única tela.
            </p>
          </div>

          <div class="hero-stats">
            <div class="stat-card">
              <small>Pedidos hoje</small>
              <strong>52</strong>
              <span class="trend up">+12% vs ontem</span>
            </div>

            <div class="stat-card">
              <small>Tempo médio de entrega</small>
              <strong>34 min</strong>
              <span class="trend up">Dentro da meta</span>
            </div>

            <div class="stat-card">
              <small>Ticket médio</small>
              <strong>R$ 31,90</strong>
              <span class="trend up">+7,4%</span>
            </div>

            <div class="stat-card">
              <small>Avaliação</small>
              <strong>4,8 ★</strong>
              <span class="trend neutral">128 avaliações</span>
            </div>
          </div>
        </section>

        <!-- Gauges -->
        <section class="gauges-section">
          <div class="section-title">
            <h3>Indicadores principais</h3>
            <p>Faturamento e performance operacional</p>
          </div>

          <div class="gauges-grid">
            <div class="gauge-card">
              <div class="gauge" style="--value: 85; --color: #ff8a00;">
                <div class="gauge-inner">
                  <strong>85%</strong>
                  <span>Diário</span>
                </div>
              </div>
              <h4>Faturamento diário</h4>
              <p>R$ 1.280 / meta R$ 1.500</p>
            </div>

            <div class="gauge-card">
              <div class="gauge" style="--value: 84; --color: #ffb547;">
                <div class="gauge-inner">
                  <strong>84%</strong>
                  <span>Mensal</span>
                </div>
              </div>
              <h4>Faturamento mensal</h4>
              <p>R$ 38.400 / meta R$ 45.000</p>
            </div>

            <div class="gauge-card">
              <div class="gauge" style="--value: 83; --color: #ff6b2c;">
                <div class="gauge-inner">
                  <strong>83%</strong>
                  <span>Anual</span>
                </div>
              </div>
              <h4>Faturamento anual</h4>
              <p>R$ 461.000 / meta R$ 550.000</p>
            </div>

            <div class="gauge-card">
              <div class="gauge" style="--value: 91; --color: #22c55e;">
                <div class="gauge-inner">
                  <strong>91%</strong>
                  <span>Ticket</span>
                </div>
              </div>
              <h4>Ticket médio</h4>
              <p>R$ 31,90 / meta R$ 35,00</p>
            </div>

            <div class="gauge-card">
              <div class="gauge" style="--value: 94; --color: #06b6d4;">
                <div class="gauge-inner">
                  <strong>94%</strong>
                  <span>Entrega</span>
                </div>
              </div>
              <h4>Entregas no prazo</h4>
              <p>30–40 min dentro da meta</p>
            </div>

            <div class="gauge-card">
              <div class="gauge" style="--value: 96; --color: #8b5cf6;">
                <div class="gauge-inner">
                  <strong>4,8</strong>
                  <span>Nota</span>
                </div>
              </div>
              <h4>Satisfação do cliente</h4>
              <p>Baseado em 128 avaliações</p>
            </div>
          </div>
        </section>

        <!-- Charts and lists -->
        <section class="analytics-grid">
          <div class="panel large">
            <div class="panel-header">
              <div>
                <h3>Pedidos por horário</h3>
                <p>Pico entre 19h e 22h</p>
              </div>
              <button class="ghost-btn">Ver detalhes</button>
            </div>

            <div class="bars-chart">
              <div class="bar-col">
                <div class="bar" style="height: 30%"></div>
                <span>17h</span>
              </div>
              <div class="bar-col">
                <div class="bar" style="height: 45%"></div>
                <span>18h</span>
              </div>
              <div class="bar-col">
                <div class="bar" style="height: 75%"></div>
                <span>19h</span>
              </div>
              <div class="bar-col">
                <div class="bar" style="height: 92%"></div>
                <span>20h</span>
              </div>
              <div class="bar-col">
                <div class="bar" style="height: 100%"></div>
                <span>21h</span>
              </div>
              <div class="bar-col">
                <div class="bar" style="height: 82%"></div>
                <span>22h</span>
              </div>
              <div class="bar-col">
                <div class="bar" style="height: 48%"></div>
                <span>23h</span>
              </div>
            </div>
          </div>

          <div class="panel">
            <div class="panel-header">
              <div>
                <h3>Produtos mais vendidos</h3>
                <p>Top itens da operação</p>
              </div>
            </div>

            <div class="product-ranking">
              <div class="rank-item">
                <div class="rank-text">
                  <strong>X-Burger Bacon</strong>
                  <span>128 vendas</span>
                </div>
                <div class="rank-bar"><i style="width: 92%"></i></div>
              </div>

              <div class="rank-item">
                <div class="rank-text">
                  <strong>Combo Clássico</strong>
                  <span>101 vendas</span>
                </div>
                <div class="rank-bar"><i style="width: 78%"></i></div>
              </div>

              <div class="rank-item">
                <div class="rank-text">
                  <strong>Batata Frita</strong>
                  <span>87 vendas</span>
                </div>
                <div class="rank-bar"><i style="width: 69%"></i></div>
              </div>

              <div class="rank-item">
                <div class="rank-text">
                  <strong>Coca-Cola 350ml</strong>
                  <span>65 vendas</span>
                </div>
                <div class="rank-bar"><i style="width: 54%"></i></div>
              </div>
            </div>
          </div>

          <div class="panel">
            <div class="panel-header">
              <div>
                <h3>Bairros com mais pedidos</h3>
                <p>Atendimento atual</p>
              </div>
            </div>

            <ul class="list-clean">
              <li><span>Centro</span> <strong>28%</strong></li>
              <li><span>Flamengo</span> <strong>19%</strong></li>
              <li><span>Botafogo</span> <strong>16%</strong></li>
              <li><span>Copacabana</span> <strong>13%</strong></li>
              <li><span>Laranjeiras</span> <strong>9%</strong></li>
              <li><span>Outros</span> <strong>15%</strong></li>
            </ul>
          </div>

          <div class="panel">
            <div class="panel-header">
              <div>
                <h3>Status da loja</h3>
                <p>Operação e publicação</p>
              </div>
            </div>

            <div class="status-stack">
              <div class="status-pill success">✅ Loja publicada</div>
              <div class="status-pill success">✅ Horários configurados</div>
              <div class="status-pill success">✅ Entrega ativa</div>
              <div class="status-pill warning">⚠ 2 produtos com baixa saída</div>
              <div class="status-pill info">ℹ Pedido mínimo: R$ 25,00</div>
            </div>
          </div>
        </section>
      </main>
    </div>
  </div>

  <script src="js/app.js"></script>
</body>
</html>
```

---

# 2) `css/styles.css`

```css
:root{
  --bg: #0f1115;
  --sidebar: #12151b;
  --card: rgba(255,255,255,0.06);
  --card-strong: rgba(255,255,255,0.09);
  --line: rgba(255,255,255,0.08);
  --text: #f5f7fb;
  --muted: #a7b0c0;
  --orange: #ff8a00;
  --orange-2: #ffb547;
  --green: #22c55e;
  --cyan: #06b6d4;
  --purple: #8b5cf6;
  --danger: #ef4444;
  --shadow: 0 20px 45px rgba(0,0,0,.32);
  --radius: 22px;
}

*{
  margin:0;
  padding:0;
  box-sizing:border-box;
}

html, body{
  width:100%;
  min-height:100%;
  font-family: 'Inter', sans-serif;
  background:
    radial-gradient(circle at top right, rgba(255,138,0,.12), transparent 25%),
    radial-gradient(circle at bottom left, rgba(139,92,246,.10), transparent 25%),
    var(--bg);
  color:var(--text);
}

img{
  display:block;
  max-width:100%;
}

button{
  font-family:inherit;
  cursor:pointer;
  border:none;
  outline:none;
}

a{
  text-decoration:none;
  color:inherit;
}

.app-shell{
  display:flex;
  min-height:100vh;
}

/* Sidebar */
.sidebar{
  width:260px;
  background:linear-gradient(180deg, #12151b 0%, #0e1015 100%);
  border-right:1px solid var(--line);
  padding:18px 14px;
  transition:.28s ease;
  position:sticky;
  top:0;
  height:100vh;
}

.sidebar.collapsed{
  width:92px;
}

.sidebar-top{
  display:flex;
  justify-content:flex-end;
  margin-bottom:22px;
}

.toggle-btn{
  width:44px;
  height:44px;
  border-radius:14px;
  background:var(--card);
  border:1px solid var(--line);
  display:grid;
  place-items:center;
}

.toggle-btn img{
  width:20px;
  opacity:.9;
}

.sidebar-nav{
  display:flex;
  flex-direction:column;
  gap:10px;
}

.nav-item{
  display:flex;
  align-items:center;
  gap:14px;
  padding:14px 14px;
  border-radius:18px;
  color:var(--muted);
  transition:.25s ease;
  border:1px solid transparent;
}

.nav-item:hover,
.nav-item.active{
  background:linear-gradient(135deg, rgba(255,138,0,.12), rgba(255,255,255,.04));
  color:var(--text);
  border-color:rgba(255,138,0,.18);
}

.nav-item img{
  width:22px;
  min-width:22px;
}

.sidebar.collapsed .nav-item span{
  display:none;
}

.sidebar.collapsed .nav-item{
  justify-content:center;
}

/* Main */
.main-content{
  flex:1;
  padding:22px;
}

.topbar{
  display:grid;
  grid-template-columns: 1fr auto 1fr;
  align-items:center;
  gap:16px;
  margin-bottom:22px;
  background:rgba(255,255,255,0.04);
  border:1px solid var(--line);
  border-radius:24px;
  padding:16px 20px;
  backdrop-filter: blur(12px);
  box-shadow: var(--shadow);
}

.topbar-left{
  display:flex;
  align-items:center;
}

.urbeat-logo{
  height:42px;
}

.topbar-center{
  text-align:center;
}

.topbar-center h1{
  font-size:1.35rem;
  font-weight:800;
}

.topbar-center p{
  font-size:.92rem;
  color:var(--muted);
  margin-top:4px;
}

.topbar-right{
  display:flex;
  justify-content:flex-end;
  align-items:center;
  gap:14px;
}

.store-logo-round{
  width:54px;
  height:54px;
  border-radius:999px;
  object-fit:cover;
  border:2px solid rgba(255,255,255,.14);
  box-shadow:0 10px 24px rgba(0,0,0,.25);
}

.profile-area{
  position:relative;
}

.profile-btn{
  width:48px;
  height:48px;
  border-radius:16px;
  background:var(--card);
  border:1px solid var(--line);
  display:grid;
  place-items:center;
}

.profile-btn img{
  width:22px;
}

.profile-menu{
  position:absolute;
  right:0;
  top:58px;
  min-width:180px;
  background:#171b23;
  border:1px solid var(--line);
  border-radius:18px;
  padding:8px;
  box-shadow:var(--shadow);
  display:none;
  z-index:20;
}

.profile-menu.open{
  display:block;
}

.profile-menu a{
  display:block;
  padding:12px 14px;
  border-radius:12px;
  color:var(--text);
}

.profile-menu a:hover{
  background:rgba(255,255,255,.06);
}

/* Dashboard */
.dashboard{
  display:flex;
  flex-direction:column;
  gap:22px;
}

.hero-panel{
  display:grid;
  grid-template-columns: 1.2fr 1fr;
  gap:18px;
  background:linear-gradient(135deg, rgba(255,138,0,.14), rgba(255,255,255,.03));
  border:1px solid rgba(255,138,0,.14);
  border-radius:28px;
  padding:24px;
  box-shadow: var(--shadow);
}

.hero-copy h2{
  font-size:1.8rem;
  margin:12px 0 8px;
}

.hero-copy p{
  color:var(--muted);
  max-width:620px;
  line-height:1.6;
}

.badge-online{
  display:inline-flex;
  align-items:center;
  gap:8px;
  color:#b6ffcb;
  background:rgba(34,197,94,.10);
  border:1px solid rgba(34,197,94,.18);
  border-radius:999px;
  padding:8px 14px;
  font-size:.86rem;
  font-weight:600;
}

.hero-stats{
  display:grid;
  grid-template-columns: repeat(2, 1fr);
  gap:14px;
}

.stat-card{
  background:rgba(255,255,255,0.05);
  border:1px solid var(--line);
  border-radius:22px;
  padding:18px;
  min-height:120px;
  display:flex;
  flex-direction:column;
  justify-content:space-between;
}

.stat-card small{
  color:var(--muted);
  font-size:.86rem;
}

.stat-card strong{
  font-size:1.55rem;
  font-weight:800;
}

.trend{
  font-size:.86rem;
  font-weight:600;
}

.trend.up{ color:#89f0a8; }
.trend.neutral{ color:#c5d0e0; }

/* Gauges */
.gauges-section,
.panel{
  background:rgba(255,255,255,0.04);
  border:1px solid var(--line);
  border-radius:28px;
  padding:22px;
  box-shadow: var(--shadow);
}

.section-title{
  margin-bottom:18px;
}

.section-title h3,
.panel-header h3{
  font-size:1.14rem;
  font-weight:800;
}

.section-title p,
.panel-header p{
  color:var(--muted);
  margin-top:4px;
  font-size:.92rem;
}

.gauges-grid{
  display:grid;
  grid-template-columns: repeat(3, 1fr);
  gap:18px;
}

.gauge-card{
  background:rgba(255,255,255,0.04);
  border:1px solid var(--line);
  border-radius:24px;
  padding:22px;
  text-align:center;
}

.gauge-card h4{
  margin-top:16px;
  font-size:1rem;
}

.gauge-card p{
  margin-top:6px;
  color:var(--muted);
  font-size:.9rem;
}

.gauge{
  --size: 160px;
  width:var(--size);
  height:var(--size);
  border-radius:50%;
  margin:0 auto;
  background:
    conic-gradient(var(--color) calc(var(--value) * 1%), rgba(255,255,255,0.08) 0);
  display:grid;
  place-items:center;
  position:relative;
}

.gauge::before{
  content:"";
  position:absolute;
  inset:12px;
  border-radius:50%;
  background:#11141b;
  box-shadow: inset 0 0 0 1px rgba(255,255,255,.05);
}

.gauge-inner{
  position:relative;
  z-index:2;
  display:flex;
  flex-direction:column;
  align-items:center;
}

.gauge-inner strong{
  font-size:1.65rem;
  font-weight:800;
}

.gauge-inner span{
  color:var(--muted);
  font-size:.92rem;
  margin-top:4px;
}

/* Analytics */
.analytics-grid{
  display:grid;
  grid-template-columns: 1.35fr 1fr;
  gap:18px;
}

.panel.large{
  min-height:340px;
}

.panel-header{
  display:flex;
  align-items:center;
  justify-content:space-between;
  gap:12px;
  margin-bottom:18px;
}

.ghost-btn{
  background:rgba(255,255,255,.05);
  color:var(--text);
  border:1px solid var(--line);
  padding:10px 14px;
  border-radius:14px;
}

.bars-chart{
  height:240px;
  display:flex;
  align-items:flex-end;
  gap:14px;
  padding-top:18px;
}

.bar-col{
  flex:1;
  display:flex;
  flex-direction:column;
  align-items:center;
  gap:10px;
}

.bar{
  width:100%;
  max-width:48px;
  border-radius:18px 18px 10px 10px;
  background:linear-gradient(180deg, var(--orange-2), var(--orange));
  box-shadow: 0 16px 26px rgba(255,138,0,.25);
}

.bar-col span{
  color:var(--muted);
  font-size:.88rem;
}

.product-ranking{
  display:flex;
  flex-direction:column;
  gap:18px;
}

.rank-item{
  display:flex;
  flex-direction:column;
  gap:10px;
}

.rank-text{
  display:flex;
  align-items:center;
  justify-content:space-between;
  gap:10px;
}

.rank-text strong{
  font-size:.96rem;
}

.rank-text span{
  color:var(--muted);
  font-size:.88rem;
}

.rank-bar{
  width:100%;
  height:10px;
  border-radius:999px;
  background:rgba(255,255,255,.08);
  overflow:hidden;
}

.rank-bar i{
  display:block;
  height:100%;
  border-radius:999px;
  background:linear-gradient(90deg, var(--orange), #ffd166);
}

.list-clean{
  list-style:none;
  display:flex;
  flex-direction:column;
  gap:12px;
}

.list-clean li{
  display:flex;
  align-items:center;
  justify-content:space-between;
  padding:14px 16px;
  border-radius:16px;
  background:rgba(255,255,255,.04);
  border:1px solid var(--line);
}

.list-clean span{
  color:var(--muted);
}

.status-stack{
  display:flex;
  flex-direction:column;
  gap:12px;
}

.status-pill{
  padding:14px 16px;
  border-radius:16px;
  font-weight:600;
  border:1px solid transparent;
}

.status-pill.success{
  background:rgba(34,197,94,.10);
  border-color:rgba(34,197,94,.18);
  color:#c9fbd7;
}

.status-pill.warning{
  background:rgba(245,158,11,.10);
  border-color:rgba(245,158,11,.18);
  color:#ffe8b0;
}

.status-pill.info{
  background:rgba(6,182,212,.10);
  border-color:rgba(6,182,212,.18);
  color:#c8f7ff;
}

/* Responsive */
@media (max-width: 1200px){
  .gauges-grid{
    grid-template-columns: repeat(2, 1fr);
  }

  .analytics-grid{
    grid-template-columns: 1fr;
  }

  .hero-panel{
    grid-template-columns: 1fr;
  }
}

@media (max-width: 860px){
  .topbar{
    grid-template-columns: 1fr;
    text-align:center;
  }

  .topbar-left,
  .topbar-right{
    justify-content:center;
  }

  .gauges-grid{
    grid-template-columns: 1fr;
  }

  .hero-stats{
    grid-template-columns: 1fr;
  }

  .sidebar{
    position:fixed;
    z-index:40;
    transform:translateX(0);
  }

  .sidebar.collapsed{
    transform:translateX(-100%);
    width:260px;
  }

  .main-content{
    padding-left:22px;
  }
}
```

---

# 3) `js/app.js`

```javascript
const sidebar = document.getElementById('sidebar');
const toggleSidebar = document.getElementById('toggleSidebar');
const profileBtn = document.getElementById('profileBtn');
const profileMenu = document.getElementById('profileMenu');

toggleSidebar.addEventListener('click', () => {
  sidebar.classList.toggle('collapsed');
});

profileBtn.addEventListener('click', (e) => {
  e.stopPropagation();
  profileMenu.classList.toggle('open');
});

document.addEventListener('click', (e) => {
  if (!profileMenu.contains(e.target) && !profileBtn.contains(e.target)) {
    profileMenu.classList.remove('open');
  }
});
```

---

# 4) `assets/logo-urbeat.svg`

```svg
<svg width="180" height="48" viewBox="0 0 180 48" fill="none" xmlns="http://www.w3.org/2000/svg">
  <rect width="48" height="48" rx="14" fill="#FF8A00"/>
  <path d="M12 30V18H16V22.5H22V18H26V30H22V25.8H16V30H12Z" fill="white"/>
  <circle cx="35" cy="16" r="4" fill="white" opacity="0.9"/>
  <text x="58" y="31" fill="white" font-size="26" font-family="Inter, Arial, sans-serif" font-weight="800">Urbeat</text>
</svg>
```

---

# 5) `assets/logo-loja-burger-house.svg`

```svg
<svg width="120" height="120" viewBox="0 0 120 120" fill="none" xmlns="http://www.w3.org/2000/svg">
  <circle cx="60" cy="60" r="58" fill="#151922" stroke="#2B3240" stroke-width="4"/>
  <g transform="translate(18,24)">
    <path d="M14 26C14 15 24 8 42 8C60 8 70 15 70 26H14Z" fill="#F59E0B" stroke="#111" stroke-width="3"/>
    <circle cx="28" cy="17" r="2" fill="white"/>
    <circle cx="40" cy="14" r="2" fill="white"/>
    <circle cx="52" cy="17" r="2" fill="white"/>
    <path d="M18 31H67L60 38H25L18 31Z" fill="#EF4444" stroke="#111" stroke-width="3"/>
    <path d="M18 39C18 39 23 45 29 45C35 45 38 39 44 39C50 39 54 45 60 45C66 45 70 39 70 39V49H18V39Z" fill="#16A34A" stroke="#111" stroke-width="3"/>
    <path d="M20 49H68L63 56H25L20 49Z" fill="#F59E0B" stroke="#111" stroke-width="3"/>
    <path d="M17 56H71V64H17V56Z" fill="#4B2E1F" stroke="#111" stroke-width="3"/>
    <path d="M16 66H72C72 73 66 78 58 78H30C22 78 16 73 16 66Z" fill="#F59E0B" stroke="#111" stroke-width="3"/>
  </g>
</svg>
```

---

# 6) `assets/icons/horarios.svg`

```svg
<svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
  <circle cx="12" cy="12" r="9" stroke="white" stroke-width="2"/>
  <path d="M12 7V12L15.5 14" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
```

---

# 7) `assets/icons/entrega.svg`

```svg
<svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
  <path d="M3 7H14V14H3V7Z" stroke="white" stroke-width="2"/>
  <path d="M14 9H18L21 12V14H14V9Z" stroke="white" stroke-width="2"/>
  <circle cx="7" cy="17" r="2" stroke="white" stroke-width="2"/>
  <circle cx="17" cy="17" r="2" stroke="white" stroke-width="2"/>
</svg>
```

---

# 8) `assets/icons/produtos.svg`

```svg
<svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
  <rect x="4" y="4" width="7" height="7" rx="2" stroke="white" stroke-width="2"/>
  <rect x="13" y="4" width="7" height="7" rx="2" stroke="white" stroke-width="2"/>
  <rect x="4" y="13" width="7" height="7" rx="2" stroke="white" stroke-width="2"/>
  <rect x="13" y="13" width="7" height="7" rx="2" stroke="white" stroke-width="2"/>
</svg>
```

---

# 9) `assets/icons/visualizar.svg`

```svg
<svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
  <path d="M2 12C4.5 7.5 8 5 12 5C16 5 19.5 7.5 22 12C19.5 16.5 16 19 12 19C8 19 4.5 16.5 2 12Z" stroke="white" stroke-width="2"/>
  <circle cx="12" cy="12" r="3" stroke="white" stroke-width="2"/>
</svg>
```

---

# 10) `assets/icons/user.svg`

```svg
<svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
  <circle cx="12" cy="8" r="4" stroke="white" stroke-width="2"/>
  <path d="M5 20C6.5 16.5 9 15 12 15C15 15 17.5 16.5 19 20" stroke="white" stroke-width="2" stroke-linecap="round"/>
</svg>
```

---

# 11) `assets/icons/menu.svg`

```svg
<svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
  <path d="M4 7H20" stroke="white" stroke-width="2" stroke-linecap="round"/>
  <path d="M4 12H20" stroke="white" stroke-width="2" stroke-linecap="round"/>
  <path d="M4 17H20" stroke="white" stroke-width="2" stroke-linecap="round"/>
</svg>
```

---

# 🌟 O que está bom nessa proposta
- 🎨 Visual moderno, com cara de **SaaS premium**
- 🍔 Totalmente alinhado ao contexto da **Burger House**
- 📊 Dashboard com foco real em operação e faturamento
- 🧭 Navegação lateral clara e objetiva
- 👤 Menu de usuário simples e funcional
- 📱 Base pronta para responsividade

---

# 💡 Dicas do que ainda falta no dashboard

## Funcionalidades que eu recomendo incluir
- 🔔 **Notificações em tempo real** de novos pedidos
- 🟢 **Botão rápido “Abrir/Fechar loja”**
- 📦 **Gestão de estoque** dos produtos
- 🎟 **Cupons e promoções**
- 🚚 **Mapa ou monitoramento de entregas**
- 📉 **Alertas de queda de vendas**
- 🧾 **Relatório exportável** em PDF/Excel
- 🏆 **Ranking de produtos por lucro**, não só por quantidade
- 👥 **Controle de usuários/permissões** da loja
- ⭐ **Painel de avaliações e comentários**
- 📲 **Atalho para WhatsApp**
- 🧠 **Sugestões inteligentes**:
  - melhor horário para promoção
  - produto com baixa saída
  - bairro com maior conversão

---

# 🖼️ Outras telas visuais que valem a pena criar
Além dessa tela principal, eu sugiro criar mais 4 telas do ecossistema:

## 1. Horários
- calendário semanal
- horários por dia
- pausas operacionais
- feriados

## 2. Entrega
- taxa por bairro
- raio de atendimento
- tempo médio
- pedido mínimo

## 3. Produtos
- cards com foto
- categorias
- status ativo/inativo
- destaque / mais vendido

## 4. Visualizar publicação
- preview da loja exatamente como o cliente verá
- banner
- logo
- destaques
- cardápio

---

# 🎨 Direção visual recomendada
## Estilo
- **Dark premium**
- Tons:
  - 🟠 laranja para destaque
  - ⚫ grafite/preto para base
  - ⚪ branco para texto
  - 🟣 roxo / 🔵 ciano para métricas secundárias

## Sensação da interface
- moderna
- confiável
- tecnológica
- gastronômica
- fácil de operar

---

# ✅ Observação de UX importante
Como você pediu:
- **logo Urbeat no lado esquerdo do cabeçalho**
- **nome da loja no centro**
- **logo da loja redondo + ícone de pessoa com menu no lado direito**

Eu organizei dessa forma porque mantém o cabeçalho equilibrado e profissional.

---

# 🚀 Próximo nível que eu sugiro
Se quiser evoluir isso para produção real, os próximos arquivos seriam:

- `horarios.html`
- `entrega.html`
- `produtos.html`
- `visualizar-publicacao.html`

Todos usando a mesma identidade visual do dashboard principal.

---

# 🖼️ Mockups visuais profissionais do dashboard