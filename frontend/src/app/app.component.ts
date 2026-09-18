import { Component, inject } from '@angular/core';
import { IonApp, IonRouterOutlet } from '@ionic/angular/standalone';
import { registerIcons } from './core/icons';
import { AppUpdateService } from './core/services/app-update.service';
import { ServerAccessService } from './core/services/server-access.service';
import { ServerAccessOverlayComponent } from './shared/components/server-access-overlay/server-access-overlay.component';

registerIcons();

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [IonApp, IonRouterOutlet, ServerAccessOverlayComponent],
  template: `
    <ion-app>
      <ion-router-outlet
        [attr.inert]="serverAccess.isLoading() ? '' : null"
        [attr.aria-busy]="serverAccess.isLoading() ? 'true' : null"
      />
      @if (serverAccess.isLoading()) {
        <app-server-access-overlay />
      }
    </ion-app>
  `,
})
export class AppComponent {
  private readonly appUpdate = inject(AppUpdateService);
  readonly serverAccess = inject(ServerAccessService);

  constructor() {
    this.appUpdate.init();
  }
}
