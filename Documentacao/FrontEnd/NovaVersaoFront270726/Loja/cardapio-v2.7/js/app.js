// Protótipo estático. Este arquivo fica preparado para futuras interações.
document.querySelectorAll('[data-back]').forEach((el) => {
  el.addEventListener('click', (event) => {
    event.preventDefault();
    if (window.history.length > 1) window.history.back();
    else window.location.href = 'index.html';
  });
});


// V2.2: rolagem horizontal da barra de categorias com mouse, trackpad e arraste.
document.querySelectorAll('.category-pills').forEach((scroller) => {
  scroller.addEventListener('wheel', (event) => {
    if (Math.abs(event.deltaY) > Math.abs(event.deltaX)) {
      scroller.scrollLeft += event.deltaY;
      event.preventDefault();
    }
  }, { passive: false });

  let isDown = false;
  let startX = 0;
  let startScrollLeft = 0;

  scroller.addEventListener('pointerdown', (event) => {
    isDown = true;
    startX = event.clientX;
    startScrollLeft = scroller.scrollLeft;
    scroller.setPointerCapture?.(event.pointerId);
  });

  scroller.addEventListener('pointermove', (event) => {
    if (!isDown) return;
    const walk = event.clientX - startX;
    scroller.scrollLeft = startScrollLeft - walk;
  });

  ['pointerup', 'pointercancel', 'pointerleave'].forEach((type) => {
    scroller.addEventListener(type, () => { isDown = false; });
  });
});
