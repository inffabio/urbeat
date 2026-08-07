import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PageHeaderComponent } from './page-header.component';

@Component({
  standalone: true,
  imports: [PageHeaderComponent],
  template: `
    <app-page-header title="Clientes" description="Acompanhe os clientes da loja.">
      <span pageHeaderMeta>Atualizado agora</span>
      <button type="button" pageHeaderActions>Atualizar</button>
    </app-page-header>
  `,
})
class TestHostComponent {}

describe('PageHeaderComponent', () => {
  it('renders page title and description', () => {
    const fixture = TestBed.createComponent(PageHeaderComponent);
    fixture.componentRef.setInput('title', 'Visao geral da loja hoje');
    fixture.componentRef.setInput('description', 'Acompanhe pedidos e faturamento em tempo real.');
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Visao geral da loja hoje');
    expect(fixture.nativeElement.textContent).toContain('Acompanhe pedidos e faturamento em tempo real.');
  });

  it('renders projected meta and actions areas', async () => {
    await TestBed.configureTestingModule({
      imports: [TestHostComponent],
    }).compileComponents();

    const fixture = TestBed.createComponent(TestHostComponent);
    fixture.detectChanges();

    const meta = fixture.nativeElement.querySelector('.page-header-meta');
    const actions = fixture.nativeElement.querySelector('.page-header-actions');

    expect(meta?.textContent).toContain('Atualizado agora');
    expect(actions?.textContent).toContain('Atualizar');
  });
});
