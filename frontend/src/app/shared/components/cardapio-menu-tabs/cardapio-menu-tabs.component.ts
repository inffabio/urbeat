import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { IonicModule } from '@ionic/angular';
import { addIcons } from 'ionicons';
import { addCircleOutline, bagHandleOutline, layersOutline } from 'ionicons/icons';

addIcons({
  'add-circle-outline': addCircleOutline,
  'bag-handle-outline': bagHandleOutline,
  'layers-outline': layersOutline,
});

@Component({
  selector: 'app-cardapio-menu-tabs',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, IonicModule],
  template: `
    <nav class="menu-tabs" aria-label="Seções do cardápio">
      <a
        routerLink="/app/cardapio/categorias"
        routerLinkActive="active"
        [routerLinkActiveOptions]="{ exact: true }"
        [attr.aria-current]="null"
      >
        <ion-icon name="layers-outline" aria-hidden="true" />
        <span>Categorias</span>
      </a>
      <a
        routerLink="/app/cardapio/produtos"
        routerLinkActive="active"
        [routerLinkActiveOptions]="{ exact: true }"
      >
        <ion-icon name="bag-handle-outline" aria-hidden="true" />
        <span>Produtos</span>
      </a>
      <a
        routerLink="/app/cardapio/adicionais"
        routerLinkActive="active"
        [routerLinkActiveOptions]="{ exact: true }"
      >
        <ion-icon name="add-circle-outline" aria-hidden="true" />
        <span>Adicionais</span>
      </a>
    </nav>
  `,
  styles: [`
    .menu-tabs {
      display: flex;
      gap: 4px;
     background: #fff;
     border: 1px solid #e7e9f3;
      border-radius: 18px;
      padding: 5px;
      overflow-x: auto;
      -webkit-overflow-scrolling: touch;
    }

    .menu-tabs a {
      display: inline-flex;
      align-items: center;
      gap: 7px;
      min-height: 42px;
      padding: 0 18px;
      border-radius: 999px;
      font-size: 14px;
      font-weight: 600;
     color: #565b70;
      text-decoration: none;
      white-space: nowrap;
      transition: background 0.15s, color 0.15s;
    }

    .menu-tabs a:hover {
       background: #eeeaff;
       color: #5b4de2;
    }

     .menu-tabs a.active {
       background: #6d5df2;
      color: #fff;
    }

    .menu-tabs a:focus-visible {
       outline: 2px solid #6d5df2;
      outline-offset: 2px;
    }

    .menu-tabs ion-icon {
      font-size: 18px;
      flex-shrink: 0;
    }
  `],
})
export class CardapioMenuTabsComponent {}
