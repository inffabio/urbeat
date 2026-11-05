import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-admin-plans-page',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './admin-plans-page.component.html',
  styleUrl: './admin-plans-page.component.scss',
})
export class AdminPlansPageComponent {}
