import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  IonContent,
  IonHeader,
  IonToolbar,
  IonTitle,
  IonButton,
  IonButtons,
  IonIcon,
  IonList,
  IonItem,
  IonItemDivider,
  IonLabel,
  IonInput,
  IonTextarea,
  IonToggle,
  IonModal,
  IonAlert,
  IonBadge,
  IonSpinner,
} from '@ionic/angular/standalone';
import { addIcons } from 'ionicons';
import { add, create, trash, close, save, createOutline } from 'ionicons/icons';

import { LandingPageService, LandingPageContent, LandingPageContentRequest } from '../../core/services/landing-page.service';

@Component({
  selector: 'app-landing-page-admin',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    IonContent,
    IonHeader,
    IonToolbar,
    IonTitle,
    IonButton,
    IonButtons,
    IonIcon,
    IonList,
    IonItem,
    IonItemDivider,
    IonLabel,
    IonInput,
    IonTextarea,
    IonToggle,
    IonModal,
    IonAlert,
    IonBadge,
    IonSpinner,
  ],
  templateUrl: './landing-page-admin.component.html',
  styleUrls: ['./landing-page-admin.component.scss'],
})
export class LandingPageAdminComponent implements OnInit {
  private readonly service = inject(LandingPageService);

  readonly items = signal<LandingPageContent[]>([]);
  readonly loading = signal(true);
  readonly isModalOpen = signal(false);
  readonly itemToDelete = signal<LandingPageContent | null>(null);

  // Form state
  formId = signal<string>('');
  formSection = signal<string>('');
  formKey = signal<string>('');
  formValue = signal<string>('');
  formDisplayOrder = signal<number>(1);
  formIsActive = signal<boolean>(true);
  formDescription = signal<string>('');

  constructor() {
    addIcons({ add, create, trash, close, save, createOutline });
  }

  ngOnInit(): void {
    this.loadItems();
  }

  private loadItems(): void {
    this.loading.set(true);
    this.service.getAll().subscribe({
      next: (data) => {
        this.items.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load landing page content', err);
        this.loading.set(false);
      },
    });
  }

  openModal(item?: LandingPageContent): void {
    if (item) {
      this.formId.set(item.id);
      this.formSection.set(item.section);
      this.formKey.set(item.key);
      this.formValue.set(item.value);
      this.formDisplayOrder.set(item.displayOrder);
      this.formIsActive.set(item.isActive);
      this.formDescription.set(item.description || '');
    } else {
      this.formId.set('');
      this.formSection.set('');
      this.formKey.set('');
      this.formValue.set('');
      this.formDisplayOrder.set(1);
      this.formIsActive.set(true);
      this.formDescription.set('');
    }
    this.isModalOpen.set(true);
  }

  closeModal(): void {
    this.isModalOpen.set(false);
  }

  saveItem(): void {
    const request: LandingPageContentRequest = {
      section: this.formSection(),
      key: this.formKey(),
      value: this.formValue(),
      displayOrder: this.formDisplayOrder(),
      isActive: this.formIsActive(),
      description: this.formDescription(),
    };

    const id = this.formId();
    if (id) {
      this.service.update(id, request).subscribe({
        next: () => {
          this.closeModal();
          this.loadItems();
        },
        error: (err) => console.error('Failed to update', err),
      });
    } else {
      this.service.create(request).subscribe({
        next: () => {
          this.closeModal();
          this.loadItems();
        },
        error: (err) => console.error('Failed to create', err),
      });
    }
  }

  confirmDelete(item: LandingPageContent): void {
    this.itemToDelete.set(item);
  }

  executeDelete(): void {
    const id = this.itemToDelete()?.id;
    if (id) {
      this.service.delete(id).subscribe({
        next: () => {
          this.itemToDelete.set(null);
          this.loadItems();
        },
        error: (err) => console.error('Failed to delete', err),
      });
    }
  }

  cancelDelete(): void {
    this.itemToDelete.set(null);
  }

  getGroupedItems(): Record<string, LandingPageContent[]> {
    const grouped: Record<string, LandingPageContent[]> = {};
    this.items().forEach((item) => {
      if (!grouped[item.section]) {
        grouped[item.section] = [];
      }
      grouped[item.section].push(item);
    });

    // Sort items within each group by displayOrder
    Object.keys(grouped).forEach((key) => {
      grouped[key].sort((a, b) => a.displayOrder - b.displayOrder);
    });

    return grouped;
  }
}
