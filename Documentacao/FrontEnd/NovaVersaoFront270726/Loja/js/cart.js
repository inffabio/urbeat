(() => {
  const readCart = () => {
    try {
      const value = JSON.parse(localStorage.getItem('brasaCart') || '[]');
      return Array.isArray(value) ? value : [];
    } catch {
      return [];
    }
  };
  const escapeHtml = (value) => String(value ?? '').replace(/[&<>"']/g, (character) => ({
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#039;'
  }[character]));
  const cart = readCart();
  const pizzaItems = cart.filter((item) => item.type === 'pizza');
  if (!pizzaItems.length) return;

  const money = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
  const list = document.querySelector('.cart-list');
  const pizzaTotal = pizzaItems.reduce((sum, item) => sum + Number(item.total || 0), 0);

  pizzaItems.slice().reverse().forEach((item) => {
    const article = document.createElement('article');
    article.className = 'cart-product-card dynamic-pizza-item';
    article.dataset.cartId = item.id;
    const flavorText = item.flavors.map((flavor) => flavor.name).join(' + ');
    const removed = item.flavors.flatMap((flavor) => flavor.removedIngredients.map((ingredient) => `sem ${ingredient}`));
    const extras = item.extras.map((extra) => `${extra.quantity}× ${extra.name}`);
    const details = [
      flavorText,
      `Massa ${item.dough}`,
      item.crust !== 'Sem borda recheada' ? `Borda ${item.crust}` : '',
      ...extras,
      ...removed,
      ...item.prep,
      item.notes
    ].filter(Boolean).join(' · ');
    article.innerHTML = `
      <img src="${escapeHtml(item.image)}" alt="${escapeHtml(item.name)}">
      <div class="cart-product-info">
        <h3>${escapeHtml(item.name)}</h3>
        <p>${escapeHtml(item.size)} · ${escapeHtml(details)}</p>
        <strong class="cart-product-price">${money.format(item.total)}</strong>
      </div>
      <button class="cart-remove" type="button" aria-label="Remover ${item.name}"><i class="bi bi-x-lg"></i></button>
      <div class="cart-qty-pill" aria-label="Quantidade de ${item.name}">
        <button class="qty-minus" type="button" aria-label="Diminuir quantidade"><i class="bi bi-dash-circle"></i></button>
        <strong>${item.quantity}</strong>
        <button class="qty-plus" type="button" aria-label="Aumentar quantidade"><i class="bi bi-plus-circle-fill"></i></button>
      </div>`;
    list.prepend(article);
  });

  function updateTotals() {
    const current = readCart().filter((item) => item.type === 'pizza');
    const dynamicTotal = current.reduce((sum, item) => sum + Number(item.total || 0), 0);
    const subtotal = 42.40 + dynamicTotal;
    const total = subtotal + 7.49;
    document.querySelector('[data-cart-subtotal]').textContent = money.format(subtotal);
    document.querySelector('[data-cart-total]').textContent = money.format(total);
  }

  list.addEventListener('click', (event) => {
    const article = event.target.closest('.dynamic-pizza-item');
    if (!article) return;
    const current = readCart();
    const item = current.find((entry) => entry.id === article.dataset.cartId);
    if (!item) return;
    if (event.target.closest('.cart-remove')) {
      localStorage.setItem('brasaCart', JSON.stringify(current.filter((entry) => entry.id !== item.id)));
      article.remove();
      updateTotals();
      return;
    }
    if (event.target.closest('.qty-minus')) item.quantity = Math.max(1, item.quantity - 1);
    if (event.target.closest('.qty-plus')) item.quantity = Math.min(20, item.quantity + 1);
    item.total = item.unitPrice * item.quantity;
    localStorage.setItem('brasaCart', JSON.stringify(current));
    article.querySelector('.cart-qty-pill strong').textContent = item.quantity;
    article.querySelector('.cart-product-price').textContent = money.format(item.total);
    updateTotals();
  });

  updateTotals();
})();
