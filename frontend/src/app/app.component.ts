import { Component, inject } from '@angular/core';
import { IonApp, IonRouterOutlet } from '@ionic/angular/standalone';
import { registerIcons } from './core/icons';
import { AppUpdateService } from './core/services/app-update.service';

registerIcons();

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [IonApp, IonRouterOutlet],
  template: `<ion-app><ion-router-outlet /></ion-app>`,
})
export class AppComponent {
  private readonly appUpdate = inject(AppUpdateService);

  constructor() {
    this.appUpdate.init();
  }
}
