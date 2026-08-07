import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { Router, RouterModule } from '@angular/router';
import { IonIcon } from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import {
  globeOutline,
  informationCircleOutline,
  locationOutline,
  printOutline,
  timeOutline,
} from 'ionicons/icons';

addIcons({
  'globe-outline': globeOutline,
  'information-circle-outline': informationCircleOutline,
  'location-outline': locationOutline,
  'print-outline': printOutline,
  'time-outline': timeOutline,
});

@Component({
  selector: 'app-config-subnav',
  standalone: true,
  imports: [CommonModule, RouterModule, IonIcon],
  template: `
    <div class="config-tabs-shell">
      <nav class="nav nav-pills config-tabs" aria-label="Sub-navegacao de configuracoes">
        <a class="nav-item nav-link" routerLink="/app/configuracoes/horarios" routerLinkActive="active">
          <ion-icon name="time-outline" aria-hidden="true" />
          <span>Horários</span>
        </a>
        <a class="nav-item nav-link" routerLink="/app/configuracoes/informacoes" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">
          <ion-icon name="information-circle-outline" aria-hidden="true" />
          <span>Informações</span>
        </a>
        <a class="nav-item nav-link" routerLink="/app/configuracoes/impressao" routerLinkActive="active">
          <ion-icon name="print-outline" aria-hidden="true" />
          <span>Impressão</span>
        </a>
        <a class="nav-item nav-link" routerLink="/app/configuracoes/bio" routerLinkActive="active">
          <ion-icon name="globe-outline" aria-hidden="true" />
          <span>Bio</span>
        </a>
        <a class="nav-item nav-link" routerLink="/app/configuracoes/bairros" routerLinkActive="active">
          <ion-icon name="location-outline" aria-hidden="true" />
          <span>Bairros</span>
        </a>
      </nav>
    </div>
  `,
  styles: [`
    .config-tabs-shell {
      padding: 4px;
      margin-bottom: 20px;
      border: 1px solid var(--dash-line, #e7e9f3);
      border-radius: 10px;
      background: var(--dash-surface, #fff);
      overflow-x: auto;
      scrollbar-width: thin;
    }

    .config-tabs {
      display: flex;
      flex-wrap: nowrap;
      gap: 4px;
      min-width: max-content;
    }

    .config-tabs .nav-item {
      display: block;
      min-height: 44px;
      padding: 0;
      color: inherit;
      text-decoration: none;
    }

    .config-tabs .nav-link {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 7px;
      min-height: 44px;
      padding: 0 14px;
      border-radius: 7px;
      color: var(--dash-muted, #7d8298);
      font-size: 13px;
      font-weight: 700;
      text-decoration: none;
      white-space: nowrap;
      transition: background-color 160ms ease, color 160ms ease;
    }

    .config-tabs .nav-item:hover {
      background: var(--dash-primary-soft, #eeeaff);
      color: var(--dash-primary-strong, #5b4de2);
    }

    .config-tabs .nav-item.active {
      background: var(--dash-primary, #6d5df2);
      color: #fff;
    }

    .config-tabs .nav-item.active:hover {
      background: var(--dash-primary-strong, #5b4de2);
      color: #fff;
    }

    .config-tabs .nav-link:focus-visible {
      outline: 3px solid color-mix(in srgb, var(--dash-primary, #6d5df2) 25%, transparent);
      outline-offset: 2px;
    }

    .config-tabs ion-icon {
      font-size: 18px;
    }
  `],
})
export class ConfigSubnavComponent {}
