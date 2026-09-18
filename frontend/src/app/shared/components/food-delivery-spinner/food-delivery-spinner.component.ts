import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-food-delivery-spinner',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="delivery-loader" role="status" aria-live="polite" [attr.aria-label]="message">
      <div class="delivery-rider-scene" aria-hidden="true">
        <svg viewBox="0 0 280 150" preserveAspectRatio="xMidYMid meet" focusable="false">
          <ellipse class="burger-shadow" cx="140" cy="118" rx="54" ry="8" />

          <g class="burger-assembly">
            <path class="burger-layer burger-top-bun" d="M90 59c5-22 26-35 51-35 24 0 44 13 50 35 1 5-3 9-9 9H99c-6 0-10-4-9-9z" />
            <path class="sesame sesame-one" d="M118 43l7-3" />
            <path class="sesame sesame-two" d="M141 37l7 2" />
            <path class="sesame sesame-three" d="M163 45l7 3" />
            <path class="burger-layer burger-lettuce" d="M86 74c9-9 19 5 29-2 10-7 19 7 29 0 10-6 20 5 31 0 7-3 13 1 18 7-14 5-26-2-36 3-11 5-20-6-31 0-10 6-20-5-30 0-6 3-10 0-10-8z" />
            <path class="burger-layer burger-tomato" d="M92 82h96c4 0 7 3 7 7s-3 7-7 7H92c-4 0-7-3-7-7s3-7 7-7z" />
            <path class="burger-layer burger-cheese" d="M88 93h104l-18 16-18-11-16 14-17-14-17 11z" />
            <path class="burger-layer burger-patty" d="M88 103h104c6 0 11 4 11 10s-5 10-11 10H88c-6 0-11-4-11-10s5-10 11-10z" />
            <path class="burger-layer burger-bottom-bun" d="M93 119h94c5 0 9 4 9 9 0 7-6 12-13 12H97c-7 0-13-5-13-12 0-5 4-9 9-9z" />
          </g>

        </svg>
      </div>

      <p>{{ message }}</p>
    </div>
  `,
  styles: [`
    :host {
      display: block;
    }

    .delivery-loader {
      display: grid;
      place-items: center;
      gap: 16px;
      padding: 20px 8px 24px;
      text-align: center;
      color: var(--app-ink, #161616);
    }

    .delivery-rider-scene {
      width: clamp(132px, min(62vw, 46vh), 260px);
      max-width: 100%;
      aspect-ratio: 280 / 150;
      display: grid;
      place-items: center;
      overflow: hidden;
    }

    svg {
      width: 100%;
      height: 100%;
    }

    .burger-shadow {
      fill: rgba(22, 22, 22, .12);
      animation: burger-shadow 1.44s ease-in-out infinite;
      transform-origin: 140px 118px;
    }

    .burger-assembly {
      animation: burger-breathe 1.44s ease-in-out infinite;
      transform-origin: 140px 84px;
    }

    .burger-layer {
      stroke-linejoin: round;
      stroke-width: 2.5;
      animation: assemble-burger 1.44s cubic-bezier(.32, 0, .18, 1) infinite;
    }

    .burger-top-bun {
      fill: #f3b55d;
      stroke: #b9782e;
      --layer-open-y: -21px;
    }

    .burger-lettuce {
      fill: #48a96b;
      stroke: #2d7f50;
      --layer-open-y: -12px;
      animation-delay: .04s;
    }

    .burger-tomato {
      fill: #d94b43;
      stroke: #a13235;
      --layer-open-y: -5px;
      animation-delay: .08s;
    }

    .burger-cheese {
      fill: #f3c849;
      stroke: #d79d25;
      --layer-open-y: 5px;
      animation-delay: .12s;
    }

    .burger-patty {
      fill: #5c352c;
      stroke: #34201c;
      --layer-open-y: 13px;
      animation-delay: .16s;
    }

    .burger-bottom-bun {
      fill: #dfa052;
      stroke: #a8652a;
      --layer-open-y: 22px;
      animation-delay: .20s;
    }

    .sesame {
      fill: none;
      stroke: #fff4cf;
      stroke-width: 3;
      stroke-linecap: round;
      animation: assemble-burger 1.44s cubic-bezier(.32, 0, .18, 1) infinite;
      --layer-open-y: -21px;
    }

    p {
      margin: 0;
      font-size: 14px;
      font-weight: 700;
      color: var(--app-slate-warm, #565049);
    }

    @media (max-height: 480px) {
      .delivery-loader {
        gap: 10px;
        padding: 10px 8px 14px;
      }

      .delivery-rider-scene {
        width: clamp(96px, min(48vw, 40vh), 180px);
      }

      p {
        font-size: 13px;
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .burger-shadow,
      .burger-assembly,
      .burger-layer,
      .sesame {
        animation: none;
      }
    }

    @keyframes assemble-burger {
      0%, 18% {
        transform: translateY(var(--layer-open-y));
        opacity: .92;
      }
      46%, 68% {
        transform: translateY(0);
        opacity: 1;
      }
      100% {
        transform: translateY(var(--layer-open-y));
        opacity: .92;
      }
    }

    @keyframes burger-breathe {
      0%, 100% {
        transform: translateY(1px) scale(.98);
      }
      46%, 68% {
        transform: translateY(0) scale(1);
      }
    }

    @keyframes burger-shadow {
      0%, 100% {
        transform: scaleX(.78);
        opacity: .08;
      }
      46%, 68% {
        transform: scaleX(1);
        opacity: .16;
      }
    }
  `],
})
export class FoodDeliverySpinnerComponent {
  @Input() message = 'Preparando o cardapio...';
}
