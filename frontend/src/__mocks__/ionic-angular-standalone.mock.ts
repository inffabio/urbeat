// Mock for @ionic/angular/standalone to prevent ionicons resolution errors in Jest
import { Component, EventEmitter, Injectable, Input, Output } from '@angular/core';

@Component({ selector: 'ion-content', template: '<ng-content></ng-content>', standalone: true })
export class IonContent {
  @Input() fullscreen = false;
}

@Component({ selector: 'ion-icon', template: '', standalone: true })
export class IonIcon {
  @Input() name?: string;
}

@Component({ selector: 'ion-modal', template: '', standalone: true })
export class IonModal {
  @Input() isOpen = false;
  @Input() cssClass?: string;
  @Input() alignment?: string;
  @Output() didDismiss = new EventEmitter<void>();
  @Output() didPresent = new EventEmitter<void>();
  @Output() ionModalDidDismiss = new EventEmitter<void>();
}

@Component({ selector: 'ion-header', template: '', standalone: true })
export class IonHeader {}

@Component({ selector: 'ion-toolbar', template: '', standalone: true })
export class IonToolbar {}

@Component({ selector: 'ion-title', template: '', standalone: true })
export class IonTitle {}

@Component({ selector: 'ion-buttons', template: '', standalone: true })
export class IonButtons {}

@Component({ selector: 'ion-button', template: '', standalone: true })
export class IonButton {}

@Component({ selector: 'ion-searchbar', template: '', standalone: true })
export class IonSearchbar {}

@Component({ selector: 'ion-list', template: '', standalone: true })
export class IonList {}

@Component({ selector: 'ion-item', template: '', standalone: true })
export class IonItem {}

@Component({ selector: 'ion-label', template: '', standalone: true })
export class IonLabel {}

@Component({ selector: 'ion-input', template: '', standalone: true })
export class IonInput {}

@Component({ selector: 'ion-spinner', template: '', standalone: true })
export class IonSpinner {}

@Component({ selector: 'ion-progress-bar', template: '', standalone: true })
export class IonProgressBar {}

@Component({ selector: 'ion-note', template: '', standalone: true })
export class IonNote {}

@Component({ selector: 'ion-reorder-group', template: '<ng-content></ng-content>', standalone: true })
export class IonReorderGroup {}

@Component({ selector: 'ion-reorder', template: '', standalone: true })
export class IonReorder {}

@Component({ selector: 'ion-select', template: '', standalone: true })
export class IonSelect {}

@Component({ selector: 'ion-select-option', template: '', standalone: true })
export class IonSelectOption {}

@Injectable({ providedIn: 'root' })
export class ToastController {
  create = jest.fn().mockResolvedValue({ present: jest.fn() });
}
