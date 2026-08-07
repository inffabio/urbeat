(() => {
  const money = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

  const sizes = {
    pequena: { name: 'Pequena', cm: 25, slices: 4, maxFlavors: 1, icon: 'bi-circle', key: 'pequena' },
    media: { name: 'Média', cm: 35, slices: 6, maxFlavors: 2, icon: 'bi-circle', key: 'media' },
    grande: { name: 'Grande', cm: 40, slices: 10, maxFlavors: 3, icon: 'bi-circle', key: 'grande' },
    gigante: { name: 'Gigante', cm: 45, slices: 12, maxFlavors: 4, icon: 'bi-circle', key: 'gigante' }
  };

  const flavors = [
    { id: 'calabresa', name: 'Calabresa', category: 'tradicional', desc: 'Muçarela, calabresa, cebola e orégano', ingredients: ['Muçarela', 'Calabresa', 'Cebola', 'Orégano', 'Azeitona'], image: 'assets/images/pizza-calabresa.jpg', prices: [43.90, 53.90, 63.90, 73.90] },
    { id: 'marguerita', name: 'Marguerita', category: 'tradicional', desc: 'Muçarela, tomate, manjericão e parmesão', ingredients: ['Muçarela', 'Tomate', 'Manjericão', 'Parmesão', 'Orégano'], image: 'assets/images/pizza-alho.jpg', prices: [44.90, 54.90, 64.90, 74.90] },
    { id: 'portuguesa', name: 'Portuguesa', category: 'especial', desc: 'Presunto, ovo, cebola, ervilha e muçarela', ingredients: ['Presunto', 'Ovo', 'Cebola', 'Ervilha', 'Muçarela', 'Azeitona'], image: 'assets/images/pizza-portuguesa.jpg', prices: [47.90, 57.90, 67.90, 77.90] },
    { id: 'quatro-queijos', name: 'Quatro Queijos', category: 'especial', desc: 'Muçarela, provolone, parmesão e catupiry', ingredients: ['Muçarela', 'Provolone', 'Parmesão', 'Catupiry', 'Orégano'], image: 'assets/images/pizza-quatro-queijos.jpg', prices: [49.90, 59.90, 69.90, 79.90] },
    { id: 'frango-catupiry', name: 'Frango com Catupiry', category: 'especial', desc: 'Frango temperado, catupiry e milho', ingredients: ['Frango', 'Catupiry', 'Milho', 'Muçarela', 'Orégano'], image: 'assets/images/pizza-portuguesa.jpg', prices: [48.90, 58.90, 68.90, 78.90] },
    { id: 'pepperoni', name: 'Pepperoni', category: 'especial', desc: 'Muçarela, pepperoni e molho da casa', ingredients: ['Muçarela', 'Pepperoni', 'Molho da casa', 'Orégano'], image: 'assets/images/pizza-calabresa.jpg', prices: [51.90, 61.90, 71.90, 81.90] },
    { id: 'vegetariana', name: 'Vegetariana', category: 'vegetariana', desc: 'Abobrinha, tomate, champignon e cebola roxa', ingredients: ['Abobrinha', 'Tomate', 'Champignon', 'Cebola roxa', 'Muçarela'], image: 'assets/images/pizza-alho.jpg', prices: [46.90, 56.90, 66.90, 76.90] },
    { id: 'chocolate', name: 'Chocolate com Morango', category: 'doce', desc: 'Chocolate ao leite, morango e leite condensado', ingredients: ['Chocolate', 'Morango', 'Leite condensado'], image: 'assets/images/pizza-quatro-queijos.jpg', prices: [45.90, 55.90, 65.90, 75.90] }
  ];

  const doughs = [
    { id: 'tradicional', name: 'Tradicional', desc: 'Macia por dentro e crocante por fora', price: 0 },
    { id: 'fina', name: 'Fina e crocante', desc: 'Mais leve e com bordas delicadas', price: 0 },
    { id: 'pan', name: 'Pan', desc: 'Alta, macia e aerada', price: 4.90 },
    { id: 'integral', name: 'Integral', desc: 'Preparada com farinha integral', price: 3.90 },
    { id: 'sem-gluten', name: 'Sem glúten', desc: 'Massa individual especial', price: 8.90 }
  ];

  const crusts = [
    { id: 'sem-borda', name: 'Sem borda recheada', desc: 'Borda tradicional', price: 0, kind: 'any' },
    { id: 'catupiry', name: 'Catupiry', desc: 'Cremosa e suave', price: 7.90, kind: 'savory' },
    { id: 'cheddar', name: 'Cheddar', desc: 'Intensa e cremosa', price: 7.90, kind: 'savory' },
    { id: 'mucarela', name: 'Muçarela', desc: 'Clássica e bem recheada', price: 8.90, kind: 'savory' },
    { id: 'cream-cheese', name: 'Cream cheese', desc: 'Leve e cremosa', price: 9.90, kind: 'savory' },
    { id: 'chocolate', name: 'Chocolate', desc: 'Disponível para pizzas doces', price: 8.90, kind: 'sweet' }
  ];

  const extras = [
    { id: 'queijo-extra', name: 'Queijo extra', price: 6.90 },
    { id: 'bacon', name: 'Bacon crocante', price: 5.90 },
    { id: 'azeitona', name: 'Azeitonas extras', price: 3.00 },
    { id: 'champignon', name: 'Champignon', price: 5.90 },
    { id: 'molho', name: 'Molho da casa extra', price: 2.00 }
  ];

  const prepOptions = [
    { id: 'bem-assada', name: 'Bem assada' },
    { id: 'pouco-assada', name: 'Pouco assada' },
    { id: 'cortada', name: 'Enviar cortada' },
    { id: 'nao-cortar', name: 'Não cortar' },
    { id: 'sem-oregano', name: 'Sem orégano' },
    { id: 'sem-azeitona-centro', name: 'Sem azeitona no centro' }
  ];

  const state = {
    size: 'grande',
    flavorCount: 3,
    activeSlot: 0,
    selectedFlavors: [null, null, null],
    removedIngredients: [[], [], []],
    dough: 'tradicional',
    crust: 'sem-borda',
    crustPortions: [true, true, true],
    extras: Object.fromEntries(extras.map((item) => [item.id, 0])),
    prep: [],
    notes: '',
    quantity: 1,
    filter: 'todos',
    search: ''
  };

  const sizeKeys = Object.keys(sizes);
  const getFlavor = (id) => flavors.find((flavor) => flavor.id === id);
  const getDough = () => doughs.find((item) => item.id === state.dough);
  const getCrust = () => crusts.find((item) => item.id === state.crust);
  const flavorPrice = (flavor) => flavor.prices[sizeKeys.indexOf(state.size)];
  const selectedFlavorObjects = () => state.selectedFlavors.map(getFlavor).filter(Boolean);
  const isAllSweet = () => {
    const chosen = selectedFlavorObjects();
    return chosen.length === state.flavorCount && chosen.every((flavor) => flavor.category === 'doce');
  };
  const isAnySweet = () => selectedFlavorObjects().some((flavor) => flavor.category === 'doce');

  function calculateUnitPrice() {
    const chosen = selectedFlavorObjects();
    const fallback = flavors[0].prices[sizeKeys.indexOf(state.size)];
    const flavorBase = chosen.length ? Math.max(...chosen.map(flavorPrice)) : fallback;
    const doughPrice = getDough().price;
    const crust = getCrust();
    const selectedPortions = state.crust === 'sem-borda' ? 0 : state.crustPortions.filter(Boolean).length;
    const crustPrice = state.crust === 'sem-borda' ? 0 : crust.price * (selectedPortions / state.flavorCount);
    const extrasPrice = extras.reduce((total, extra) => total + extra.price * state.extras[extra.id], 0);
    return flavorBase + doughPrice + crustPrice + extrasPrice;
  }

  function renderSizes() {
    const container = document.querySelector('#sizeOptions');
    container.innerHTML = sizeKeys.map((key) => {
      const size = sizes[key];
      const basePrice = flavors[0].prices[sizeKeys.indexOf(key)];
      return `<button type="button" class="pizza-size-card ${state.size === key ? 'active' : ''}" data-size="${key}" role="radio" aria-checked="${state.size === key}">
        <i class="bi ${size.icon} size-icon" aria-hidden="true"></i>
        <strong>${size.name} · ${size.cm} cm</strong>
        <small>${size.slices} fatias · até ${size.maxFlavors} ${size.maxFlavors === 1 ? 'sabor' : 'sabores'}</small>
        <span class="price-from">A partir de ${money.format(basePrice)}</span>
        <span class="selection-check"><i class="bi bi-check-lg" aria-hidden="true"></i></span>
      </button>`;
    }).join('');
  }

  function renderFlavorCounts() {
    const max = sizes[state.size].maxFlavors;
    document.querySelector('#flavorCountHelp').textContent = `O tamanho ${sizes[state.size].name} aceita até ${max} ${max === 1 ? 'sabor' : 'sabores'}.`;
    document.querySelector('#flavorCountOptions').innerHTML = [1, 2, 3, 4].map((count) => {
      const disabled = count > max;
      const label = count === 1 ? 'Inteira' : count === 2 ? 'Meio a meio' : count === 3 ? '3 partes' : '4 partes';
      return `<button type="button" class="flavor-count ${state.flavorCount === count ? 'active' : ''}" data-count="${count}" role="radio" aria-checked="${state.flavorCount === count}" ${disabled ? 'disabled' : ''}>
        <strong>${count}</strong><small>${label}</small>
      </button>`;
    }).join('');
  }

  function slotLabel(index) {
    if (state.flavorCount === 1) return 'Pizza inteira';
    if (state.flavorCount === 2) return `Metade ${index + 1}`;
    return `Parte ${index + 1} de ${state.flavorCount}`;
  }

  function renderSlots() {
    document.querySelector('#flavorSlotTabs').innerHTML = state.selectedFlavors.map((id, index) => {
      const flavor = getFlavor(id);
      return `<button type="button" class="flavor-slot ${state.activeSlot === index ? 'active' : ''} ${flavor ? 'complete' : ''}" data-slot="${index}" role="tab" aria-selected="${state.activeSlot === index}">
        ${slotLabel(index)}<span>${flavor ? flavor.name : 'Escolher sabor'}</span>
      </button>`;
    }).join('');
  }

  function renderFlavors() {
    const normalizedSearch = state.search.trim().toLocaleLowerCase('pt-BR');
    const visible = flavors.filter((flavor) => {
      const categoryMatches = state.filter === 'todos' || flavor.category === state.filter;
      const haystack = `${flavor.name} ${flavor.desc}`.toLocaleLowerCase('pt-BR');
      return categoryMatches && haystack.includes(normalizedSearch);
    });
    document.querySelector('#flavorList').innerHTML = visible.length ? visible.map((flavor) => {
      const active = state.selectedFlavors[state.activeSlot] === flavor.id;
      return `<button type="button" class="flavor-card ${active ? 'active' : ''}" data-flavor="${flavor.id}" role="radio" aria-checked="${active}">
        <img src="${flavor.image}" alt="">
        <span><h3>${flavor.name}</h3><p>${flavor.desc}</p></span>
        <span class="flavor-price">${money.format(flavorPrice(flavor))}</span>
      </button>`;
    }).join('') : '<p class="config-help">Nenhum sabor encontrado.</p>';
  }

  function renderCustomizations() {
    const container = document.querySelector('#flavorCustomizations');
    const cards = state.selectedFlavors.map((id, index) => {
      const flavor = getFlavor(id);
      if (!flavor) return '';
      return `<article class="customization-card">
        <strong>${slotLabel(index)} · ${flavor.name}</strong>
        <p>Toque para retirar um ingrediente desta parte:</p>
        <div class="ingredient-chips">${flavor.ingredients.map((ingredient) => {
          const removed = state.removedIngredients[index].includes(ingredient);
          return `<button type="button" class="ingredient-chip ${removed ? 'removed' : ''}" data-slot="${index}" data-ingredient="${ingredient}" aria-pressed="${removed}">${removed ? 'Sem ' : ''}${ingredient}</button>`;
        }).join('')}</div>
      </article>`;
    }).join('');
    container.innerHTML = cards;
  }

  function renderDoughs() {
    document.querySelector('#doughOptions').innerHTML = doughs.map((item) => `<label class="choice-row ${state.dough === item.id ? 'checked' : ''}">
      <input type="radio" name="pizza-dough" value="${item.id}" ${state.dough === item.id ? 'checked' : ''}>
      <span class="choice-dot"></span>
      <span><strong>${item.name}</strong><small>${item.desc}</small></span>
      <span class="choice-price">${item.price ? `+ ${money.format(item.price)}` : 'Inclusa'}</span>
    </label>`).join('');
  }

  function crustIsDisabled(item) {
    if (item.kind === 'sweet') return !isAllSweet();
    if (item.kind === 'savory') return isAnySweet();
    return false;
  }

  function renderCrusts() {
    if (crustIsDisabled(getCrust())) state.crust = 'sem-borda';
    document.querySelector('#crustOptions').innerHTML = crusts.map((item) => {
      const disabled = crustIsDisabled(item);
      return `<label class="choice-row ${state.crust === item.id ? 'checked' : ''} ${disabled ? 'is-disabled' : ''}">
        <input type="radio" name="pizza-crust" value="${item.id}" ${state.crust === item.id ? 'checked' : ''} ${disabled ? 'disabled' : ''}>
        <span class="choice-dot"></span>
        <span><strong>${item.name}</strong><small>${disabled && item.kind === 'sweet' ? 'Disponível quando todos os sabores forem doces' : disabled ? 'Indisponível para pizza doce' : item.desc}</small></span>
        <span class="choice-price">${item.price ? `+ ${money.format(item.price)}` : 'Inclusa'}</span>
      </label>`;
    }).join('');
    const portions = document.querySelector('#crustPortions');
    portions.hidden = state.crust === 'sem-borda';
    document.querySelector('#crustPortionOptions').innerHTML = `<div class="portion-grid">${state.crustPortions.map((checked, index) => `<label class="portion-option">
      <input type="checkbox" data-crust-portion="${index}" ${checked ? 'checked' : ''}>
      <span>${slotLabel(index)}</span>
    </label>`).join('')}</div>`;
  }

  function renderExtras() {
    document.querySelector('#extraOptions').innerHTML = extras.map((item) => {
      const quantity = state.extras[item.id];
      return `<div class="extra-row">
        <div><strong>${item.name}</strong><p>+ ${money.format(item.price)} cada</p></div>
        <div class="mini-stepper" aria-label="Quantidade de ${item.name}">
          <button type="button" data-extra="${item.id}" data-delta="-1" aria-label="Diminuir ${item.name}" ${quantity === 0 ? 'disabled' : ''}>−</button>
          <strong>${quantity}</strong>
          <button type="button" data-extra="${item.id}" data-delta="1" aria-label="Aumentar ${item.name}" ${quantity === 3 ? 'disabled' : ''}>+</button>
        </div>
      </div>`;
    }).join('');
  }

  function renderPrep() {
    document.querySelector('#prepOptions').innerHTML = prepOptions.map((item) => `<label class="prep-option ${state.prep.includes(item.id) ? 'checked' : ''}">
      <input type="checkbox" value="${item.id}" ${state.prep.includes(item.id) ? 'checked' : ''}>
      <span>${item.name}</span>
    </label>`).join('');
  }

  function updateSummary() {
    const size = sizes[state.size];
    const chosen = selectedFlavorObjects();
    const unitPrice = calculateUnitPrice();
    const total = unitPrice * state.quantity;
    const chosenNames = chosen.length ? chosen.map((flavor) => flavor.name).join(' + ') : 'Aguardando sabores';
    const crust = getCrust();
    const extrasSummary = extras.filter((item) => state.extras[item.id]).map((item) => `${state.extras[item.id]}× ${item.name}`).join(', ');

    document.querySelector('#headingPrice').textContent = money.format(unitPrice);
    document.querySelector('#pizzaTotal').textContent = money.format(total);
    document.querySelector('#pizzaQty').textContent = state.quantity;
    document.querySelector('#heroSummary').textContent = `${size.name} · até ${size.maxFlavors} ${size.maxFlavors === 1 ? 'sabor' : 'sabores'}`;
    if (chosen[0]) {
      document.querySelector('#pizzaHeroImage').src = chosen.length > 1 ? 'assets/images/pizza-meio-a-meio.png' : chosen[0].image;
      document.querySelector('#pizzaHeroImage').alt = chosen.length > 1 ? `Pizza com ${chosenNames}` : `Pizza de ${chosen[0].name}`;
    }
    document.querySelector('#pizzaSummaryLines').innerHTML = `
      <div class="summary-line"><span>Tamanho</span><strong>${size.name} · ${size.cm} cm · ${size.slices} fatias</strong></div>
      <div class="summary-line"><span>Sabores</span><strong>${chosenNames}</strong></div>
      <div class="summary-line"><span>Massa</span><strong>${getDough().name}</strong></div>
      <div class="summary-line"><span>Borda</span><strong>${crust.name}${state.crust !== 'sem-borda' && state.crustPortions.filter(Boolean).length < state.flavorCount ? ' · parcial' : ''}</strong></div>
      ${extrasSummary ? `<div class="summary-line"><span>Adicionais</span><strong>${extrasSummary}</strong></div>` : ''}
      <div class="summary-line"><span>Quantidade</span><strong>${state.quantity}</strong></div>
      <div class="summary-line total"><span>Total</span><strong>${money.format(total)}</strong></div>`;

    const complete = state.selectedFlavors.every(Boolean);
    const button = document.querySelector('#addPizzaButton');
    const label = document.querySelector('#addPizzaLabel');
    button.classList.toggle('is-incomplete', !complete);
    label.firstChild.textContent = complete ? 'Adicionar ao carrinho' : `Escolha ${state.selectedFlavors.filter((id) => !id).length} ${state.selectedFlavors.filter((id) => !id).length === 1 ? 'sabor' : 'sabores'}`;
  }

  function renderAll() {
    renderSizes();
    renderFlavorCounts();
    renderSlots();
    renderFlavors();
    renderCustomizations();
    renderDoughs();
    renderCrusts();
    renderExtras();
    renderPrep();
    updateSummary();
  }

  function resizeFlavorState(count) {
    state.flavorCount = count;
    state.selectedFlavors = Array.from({ length: count }, (_, index) => state.selectedFlavors[index] || null);
    state.removedIngredients = Array.from({ length: count }, (_, index) => state.removedIngredients[index] || []);
    state.crustPortions = Array.from({ length: count }, (_, index) => state.crustPortions[index] ?? true);
    state.activeSlot = Math.min(state.activeSlot, count - 1);
  }

  document.querySelector('#sizeOptions').addEventListener('click', (event) => {
    const button = event.target.closest('[data-size]');
    if (!button) return;
    state.size = button.dataset.size;
    const max = sizes[state.size].maxFlavors;
    if (state.flavorCount > max) resizeFlavorState(max);
    renderAll();
  });

  document.querySelector('#flavorCountOptions').addEventListener('click', (event) => {
    const button = event.target.closest('[data-count]');
    if (!button || button.disabled) return;
    resizeFlavorState(Number(button.dataset.count));
    renderAll();
  });

  document.querySelector('#flavorSlotTabs').addEventListener('click', (event) => {
    const button = event.target.closest('[data-slot]');
    if (!button) return;
    state.activeSlot = Number(button.dataset.slot);
    renderSlots();
    renderFlavors();
  });

  document.querySelector('#flavorList').addEventListener('click', (event) => {
    const button = event.target.closest('[data-flavor]');
    if (!button) return;
    state.selectedFlavors[state.activeSlot] = button.dataset.flavor;
    state.removedIngredients[state.activeSlot] = [];
    const nextEmpty = state.selectedFlavors.findIndex((id, index) => index > state.activeSlot && !id);
    if (nextEmpty >= 0) state.activeSlot = nextEmpty;
    renderAll();
  });

  document.querySelector('#flavorCustomizations').addEventListener('click', (event) => {
    const button = event.target.closest('[data-ingredient]');
    if (!button) return;
    const slot = Number(button.dataset.slot);
    const ingredient = button.dataset.ingredient;
    const list = state.removedIngredients[slot];
    state.removedIngredients[slot] = list.includes(ingredient) ? list.filter((item) => item !== ingredient) : [...list, ingredient];
    renderCustomizations();
    updateSummary();
  });

  document.querySelector('#flavorFilter').addEventListener('click', (event) => {
    const button = event.target.closest('[data-filter]');
    if (!button) return;
    state.filter = button.dataset.filter;
    document.querySelectorAll('#flavorFilter button').forEach((item) => item.classList.toggle('active', item === button));
    renderFlavors();
  });

  document.querySelector('#flavorSearch').addEventListener('input', (event) => {
    state.search = event.target.value;
    renderFlavors();
  });

  document.querySelector('#doughOptions').addEventListener('change', (event) => {
    state.dough = event.target.value;
    renderDoughs();
    updateSummary();
  });

  document.querySelector('#crustOptions').addEventListener('change', (event) => {
    state.crust = event.target.value;
    renderCrusts();
    updateSummary();
  });

  document.querySelector('#crustPortionOptions').addEventListener('change', (event) => {
    const index = Number(event.target.dataset.crustPortion);
    state.crustPortions[index] = event.target.checked;
    if (!state.crustPortions.some(Boolean)) state.crust = 'sem-borda';
    renderCrusts();
    updateSummary();
  });

  document.querySelector('#extraOptions').addEventListener('click', (event) => {
    const button = event.target.closest('[data-extra]');
    if (!button) return;
    const id = button.dataset.extra;
    const next = state.extras[id] + Number(button.dataset.delta);
    state.extras[id] = Math.max(0, Math.min(3, next));
    renderExtras();
    updateSummary();
  });

  document.querySelector('#prepOptions').addEventListener('change', (event) => {
    const id = event.target.value;
    const opposites = { 'bem-assada': 'pouco-assada', 'pouco-assada': 'bem-assada', 'cortada': 'nao-cortar', 'nao-cortar': 'cortada' };
    if (event.target.checked) {
      state.prep = [...state.prep.filter((item) => item !== opposites[id]), id];
    } else {
      state.prep = state.prep.filter((item) => item !== id);
    }
    renderPrep();
    updateSummary();
  });

  document.querySelector('#pizzaNotes').addEventListener('input', (event) => {
    state.notes = event.target.value;
    document.querySelector('#notesCounter').textContent = `${state.notes.length}/160`;
  });

  document.querySelector('#qtyMinus').addEventListener('click', () => {
    state.quantity = Math.max(1, state.quantity - 1);
    updateSummary();
  });
  document.querySelector('#qtyPlus').addEventListener('click', () => {
    state.quantity = Math.min(20, state.quantity + 1);
    updateSummary();
  });

  document.querySelector('#addPizzaButton').addEventListener('click', () => {
    if (!state.selectedFlavors.every(Boolean)) {
      const target = document.querySelector('#flavorsSection');
      target.classList.remove('field-error');
      void target.offsetWidth;
      target.classList.add('field-error');
      target.scrollIntoView({ behavior: 'smooth', block: 'start' });
      return;
    }
    const size = sizes[state.size];
    const unitPrice = calculateUnitPrice();
    const item = {
      id: `pizza-${Date.now()}`,
      type: 'pizza',
      name: `Pizza ${size.name}`,
      size: `${size.cm} cm · ${size.slices} fatias`,
      flavors: state.selectedFlavors.map((id, index) => ({
        name: getFlavor(id).name,
        removedIngredients: state.removedIngredients[index]
      })),
      dough: getDough().name,
      crust: getCrust().name,
      crustPortions: state.crustPortions,
      extras: extras.filter((extra) => state.extras[extra.id]).map((extra) => ({ name: extra.name, quantity: state.extras[extra.id] })),
      prep: prepOptions.filter((item) => state.prep.includes(item.id)).map((item) => item.name),
      notes: state.notes,
      quantity: state.quantity,
      unitPrice,
      total: unitPrice * state.quantity,
      image: state.selectedFlavors.length > 1 ? 'assets/images/pizza-meio-a-meio.png' : getFlavor(state.selectedFlavors[0]).image
    };
    let cart = [];
    try {
      const saved = JSON.parse(localStorage.getItem('brasaCart') || '[]');
      if (Array.isArray(saved)) cart = saved;
    } catch {
      cart = [];
    }
    cart.push(item);
    localStorage.setItem('brasaCart', JSON.stringify(cart));
    const button = document.querySelector('#addPizzaButton');
    button.classList.add('added');
    document.querySelector('#addPizzaLabel').firstChild.textContent = 'Pizza adicionada!';
    setTimeout(() => { window.location.href = 'carrinho.html'; }, 450);
  });

  const requestedSize = new URLSearchParams(window.location.search).get('tamanho');
  if (requestedSize && sizes[requestedSize]) {
    state.size = requestedSize;
    resizeFlavorState(sizes[requestedSize].maxFlavors);
  }
  renderAll();
})();
