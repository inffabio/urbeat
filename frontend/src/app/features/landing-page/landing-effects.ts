/**
 * Efeitos de interação da Landing Page (Migrado do app.js original)
 * Gerencia event listeners e retorna uma função de cleanup para evitar vazamentos de memória.
 */
export function initLandingEffects(root: HTMLElement): () => void {
  const cleanups: Array<() => void> = [];

  // ─── 1. Header scrolled state ──────────────────────────────
  const header = root.querySelector<HTMLElement>('#header');
  if (header) {
    const onScroll = () => {
      // O scroll é do próprio componente (:host), não da window
      header.classList.toggle('scrolled', root.scrollTop > 8);
    };
    root.addEventListener('scroll', onScroll, { passive: true });
    onScroll(); // estado inicial
    cleanups.push(() => root.removeEventListener('scroll', onScroll));
  }

  // ─── 2. Mobile menu toggle ─────────────────────────────────
  const toggle = root.querySelector<HTMLButtonElement>('#mobile-toggle');
  const mobileMenu = root.querySelector<HTMLElement>('#mobile-menu');

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

    const onToggleClick = () => {
      mobileMenu.classList.contains('open') ? closeMenu() : openMenu();
    };

    toggle.addEventListener('click', onToggleClick);
    cleanups.push(() => toggle.removeEventListener('click', onToggleClick));

    // Fecha ao clicar em qualquer link do mobile menu
    const mobileLinks = mobileMenu.querySelectorAll('a');
    mobileLinks.forEach((link) => {
      link.addEventListener('click', closeMenu);
    });
    cleanups.push(() => {
      mobileLinks.forEach((link) => link.removeEventListener('click', closeMenu));
    });

    // Fecha ao redimensionar pra desktop
    let resizeTimer: ReturnType<typeof setTimeout>;
    const onResize = () => {
      clearTimeout(resizeTimer);
      resizeTimer = setTimeout(() => {
        if (window.innerWidth > 960) closeMenu();
      }, 100);
    };
    window.addEventListener('resize', onResize);
    cleanups.push(() => {
      window.removeEventListener('resize', onResize);
      clearTimeout(resizeTimer);
    });
  }

  // ─── 3. FAQ accordion ──────────────────────────────────────
  const faqItems = root.querySelectorAll<HTMLElement>('.faq-item');
  faqItems.forEach((item) => {
    const q = item.querySelector<HTMLButtonElement>('.faq-q');
    const a = item.querySelector<HTMLElement>('.faq-a');
    if (!q || !a) return;

    const onClick = () => {
      const isOpen = item.classList.contains('open');

      // Fecha todos os outros (modo "1 aberto por vez")
      root.querySelectorAll<HTMLElement>('.faq-item.open').forEach((other) => {
        if (other !== item) {
          other.classList.remove('open');
          const oa = other.querySelector<HTMLElement>('.faq-a');
          if (oa) oa.style.maxHeight = '';
        }
      });

      if (isOpen) {
        item.classList.remove('open');
        a.style.maxHeight = '';
      } else {
        item.classList.add('open');
        a.style.maxHeight = a.scrollHeight + 'px';
      }
    };

    q.addEventListener('click', onClick);
    cleanups.push(() => q.removeEventListener('click', onClick));
  });

  // Abre o primeiro FAQ por padrão (com delay para garantir renderização)
  const firstFaq = root.querySelector<HTMLElement>('.faq-item');
  if (firstFaq) {
    const a = firstFaq.querySelector<HTMLElement>('.faq-a');
    firstFaq.classList.add('open');
    if (a) {
      requestAnimationFrame(() => {
        a.style.maxHeight = a.scrollHeight + 'px';
      });
    }
  }

  // ─── 4. Toggle de período dos planos (Mensal / Anual) ──────
  const billingToggle = root.querySelector<HTMLElement>('#billing-toggle');
  if (billingToggle) {
    const btns = billingToggle.querySelectorAll<HTMLButtonElement>('button');
    btns.forEach((btn) => {
      const onClick = () => {
        const period = (btn.dataset as any).period;
        if (!period) return;

        (billingToggle.dataset as any).period = period;
        btns.forEach((b) => b.classList.toggle('active', (b.dataset as any).period === period));

        // Atualiza preços e "per"
        root.querySelectorAll<HTMLElement>('.plan-card').forEach((card) => {
          const val = card.querySelector<HTMLElement>('.plan-price .val');
          const per = card.querySelector<HTMLElement>('.plan-price .per');
          if (!val) return;

          const monthly = (val.dataset as any).monthly;
          const yearly = (val.dataset as any).yearly;

          if (period === 'yearly' && yearly) {
            val.textContent = yearly;
            if (per) per.textContent = '/mês · cobrado anual';
          } else if (monthly) {
            val.textContent = monthly;
            if (per) per.textContent = '/mês';
          }
        });
      };
      btn.addEventListener('click', onClick);
      cleanups.push(() => btn.removeEventListener('click', onClick));
    });
  }

  // ─── 5. Scroll reveal (Intersection Observer) ──────────────
  const reveals = root.querySelectorAll<HTMLElement>('.reveal');
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
      }
    );
    reveals.forEach((el) => io.observe(el));
    cleanups.push(() => io.disconnect());
  } else {
    // fallback: mostra tudo
    reveals.forEach((el) => el.classList.add('in'));
  }

  // ─── 6. Demo carousel navigation arrows ────────────────────
  const demoPrev = root.querySelector<HTMLButtonElement>('#demo-prev');
  const demoNext = root.querySelector<HTMLButtonElement>('#demo-next');
  const demoTrack = root.querySelector<HTMLElement>('#demo-track');

  if (demoPrev && demoNext && demoTrack) {
    const scrollDemo = (direction: number) => {
      const phone = demoTrack.querySelector<HTMLElement>('.phone');
      if (!phone) return;
      const step = phone.offsetWidth + parseFloat(getComputedStyle(demoTrack).gap || '0');
      demoTrack.scrollBy({ left: direction * step, behavior: 'smooth' });
    };

    const onPrevClick = () => scrollDemo(-1);
    const onNextClick = () => scrollDemo(1);

    demoPrev.addEventListener('click', onPrevClick);
    demoNext.addEventListener('click', onNextClick);

    cleanups.push(() => {
      demoPrev.removeEventListener('click', onPrevClick);
      demoNext.removeEventListener('click', onNextClick);
    });
  }

  // ─── 7. Smooth scroll para anchors do header (offset fixo) ─
  const anchorLinks = root.querySelectorAll<HTMLAnchorElement>('a[href^="#"]');
  anchorLinks.forEach((a) => {
    const onClick = (e: Event) => {
      const href = a.getAttribute('href');
      if (!href || href === '#') return;

      // Se for um link de navegação do Angular (ex: routerLink), não interceptamos
      if (a.hasAttribute('routerlink')) return;

      const target = root.querySelector<HTMLElement>(href);
      if (!target) return;

      e.preventDefault();

      const offset = 72; // altura do header
      const top = target.offsetTop - offset;
      root.scrollTo({ top, behavior: 'smooth' });
    };

    a.addEventListener('click', onClick);
    cleanups.push(() => a.removeEventListener('click', onClick));
  });

  /**
   * Retorna a função de limpeza.
   * Isso evita listeners duplicados e vazamentos de memória quando o usuário sai da página.
   */
  return () => {
    cleanups.forEach((cleanup) => cleanup());
  };
}