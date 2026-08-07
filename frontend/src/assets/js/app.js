/* ═══════════════════════════════════════════════════════════════
   HAPP'EE Landpage — interactions
   ═══════════════════════════════════════════════════════════════ */

(() => {
  'use strict';

  // ─── Header scrolled state ──────────────────────────────────
  const header = document.getElementById('header');
  if (header) {
    const onScroll = () => {
      header.classList.toggle('scrolled', window.scrollY > 8);
    };
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
  }

  // ─── Mobile menu toggle ─────────────────────────────────────
  const toggle = document.getElementById('mobile-toggle');
  const mobileMenu = document.getElementById('mobile-menu');

  if (toggle && mobileMenu) {
    const closeMenu = () => {
      mobileMenu.classList.remove('open');
      mobileMenu.setAttribute('aria-hidden', 'true');
      toggle.setAttribute('aria-expanded', 'false');
      toggle.classList.remove('open');
    };
    const openMenu = () => {
      mobileMenu.classList.add('open');
      mobileMenu.setAttribute('aria-hidden', 'false');
      toggle.setAttribute('aria-expanded', 'true');
      toggle.classList.add('open');
    };

    toggle.addEventListener('click', () => {
      mobileMenu.classList.contains('open') ? closeMenu() : openMenu();
    });

    // Fecha ao clicar em qualquer link do mobile menu
    mobileMenu.querySelectorAll('a').forEach((link) => {
      link.addEventListener('click', closeMenu);
    });

    // Fecha ao redimensionar pra desktop
    let resizeTimer;
    window.addEventListener('resize', () => {
      clearTimeout(resizeTimer);
      resizeTimer = setTimeout(() => {
        if (window.innerWidth > 960) closeMenu();
      }, 100);
    });
  }

  // ─── FAQ accordion ──────────────────────────────────────────
  document.querySelectorAll('.faq-item').forEach((item) => {
    const q = item.querySelector('.faq-q');
    const a = item.querySelector('.faq-a');
    if (!q || !a) return;

    q.addEventListener('click', () => {
      const isOpen = item.classList.contains('open');

      // Fecha todos os outros (modo "1 aberto por vez")
      document.querySelectorAll('.faq-item.open').forEach((other) => {
        if (other !== item) {
          other.classList.remove('open');
          const oa = other.querySelector('.faq-a');
          if (oa) oa.style.maxHeight = null;
        }
      });

      if (isOpen) {
        item.classList.remove('open');
        a.style.maxHeight = null;
      } else {
        item.classList.add('open');
        a.style.maxHeight = a.scrollHeight + 'px';
      }
    });
  });

  // Abre o primeiro FAQ por padrão
  const firstFaq = document.querySelector('.faq-item');
  if (firstFaq) {
    const a = firstFaq.querySelector('.faq-a');
    firstFaq.classList.add('open');
    if (a) {
      // espera o reveal terminar pra calcular altura correta
      requestAnimationFrame(() => {
        a.style.maxHeight = a.scrollHeight + 'px';
      });
    }
  }

  // ─── Toggle de período dos planos (Mensal / Anual) ──────────
  const billingToggle = document.getElementById('billing-toggle');
  if (billingToggle) {
    const btns = billingToggle.querySelectorAll('button');
    btns.forEach((btn) => {
      btn.addEventListener('click', () => {
        const period = btn.dataset.period;
        if (!period) return;

        billingToggle.dataset.period = period;
        btns.forEach((b) => b.classList.toggle('active', b.dataset.period === period));

        // Atualiza preços e "per"
        document.querySelectorAll('.plan-card').forEach((card) => {
          const val = card.querySelector('.plan-price .val');
          const per = card.querySelector('.plan-price .per');
          if (!val) return;

          const monthly = val.dataset.monthly;
          const yearly = val.dataset.yearly;

          if (period === 'yearly' && yearly) {
            val.textContent = yearly;
            if (per) per.textContent = '/mês · cobrado anual';
          } else if (monthly) {
            val.textContent = monthly;
            if (per) per.textContent = '/mês';
          }
        });
      });
    });
  }

  // ─── Scroll reveal (Intersection Observer) ──────────────────
  const reveals = document.querySelectorAll('.reveal');
  if ('IntersectionObserver' in window && reveals.length > 0) {
    const io = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add('in');
            io.unobserve(entry.target);
          }
        });
      },
      {
        rootMargin: '0px 0px -8% 0px',
        threshold: 0.08,
      },
    );
    reveals.forEach((el) => io.observe(el));
  } else {
    // fallback: mostra tudo
    reveals.forEach((el) => el.classList.add('in'));
  }

  // ─── Demo carousel navigation arrows ────────────────────────
  const demoPrev = document.getElementById('demo-prev');
  const demoNext = document.getElementById('demo-next');
  const demoTrack = document.getElementById('demo-track');

  if (demoPrev && demoNext && demoTrack) {
    const scrollDemo = (direction) => {
      const phone = demoTrack.querySelector('.phone');
      if (!phone) return;
      const step = phone.offsetWidth + parseFloat(getComputedStyle(demoTrack).gap);
      demoTrack.scrollBy({ left: direction * step, behavior: 'smooth' });
    };

    demoPrev.addEventListener('click', () => scrollDemo(-1));
    demoNext.addEventListener('click', () => scrollDemo(1));
  }

  // ─── Smooth scroll para anchors do header (offset fixo) ─────
  document.querySelectorAll('a[href^="#"]').forEach((a) => {
    a.addEventListener('click', (e) => {
      const href = a.getAttribute('href');
      if (!href || href === '#') return;
      const target = document.querySelector(href);
      if (!target) return;
      e.preventDefault();

      const offset = 72; // altura do header
      const top = target.getBoundingClientRect().top + window.scrollY - offset;
      window.scrollTo({ top, behavior: 'smooth' });
    });
  });
})();
