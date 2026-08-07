import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { OrderService } from '../../core/services/order.service';
import { AddressService } from '../../core/services/address.service';
import { ToastService } from '../../core/services/toast.service';
import { SellerCustomersPageComponent } from './seller-customers-page.component';

describe('SellerCustomersPageComponent', () => {
  let orderServiceMock: { getStoreCustomers: jest.Mock; updateStoreCustomer: jest.Mock; toggleStoreCustomer: jest.Mock };

  const buildResponse = (overrides: Partial<any> = {}) => ({
    page: 1,
    pageSize: 7,
    totalItems: 1,
    totalPages: 1,
    metrics: {
      totalCustomers: 1,
      activeCustomers: 1,
      recurringCustomers: 0,
      newCustomersThisMonth: 1,
      averageTicket: 92.5,
    },
    items: [
      {
        id: 'customer1',
        name: 'Cliente Teste',
        email: 'cliente@teste.com',
         phone: '11988887777',
         cep: '',
         street: '',
         number: '',
         complement: '',
         neighborhood: '',
         city: '',
         state: '',
        totalOrders: 2,
        totalSpent: 92.5,
        lastOrderAtUtc: '2026-07-30T10:00:00Z',
        isActive: true,
      },
    ],
    ...overrides,
  });

  beforeEach(async () => {
    orderServiceMock = {
      getStoreCustomers: jest.fn(),
      updateStoreCustomer: jest.fn(),
      toggleStoreCustomer: jest.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [SellerCustomersPageComponent],
      providers: [
        { provide: OrderService, useValue: orderServiceMock },
        { provide: AddressService, useValue: { lookupCep: jest.fn() } },
        { provide: ToastService, useValue: { showSuccess: jest.fn(), showError: jest.fn() } },
      ],
    }).compileComponents();
  });

  it('loads seller customer aggregates', () => {
    orderServiceMock.getStoreCustomers.mockReturnValue(of(buildResponse()));

    const fixture = TestBed.createComponent(SellerCustomersPageComponent);
    fixture.detectChanges();

    expect(orderServiceMock.getStoreCustomers).toHaveBeenCalledWith({
      page: 1,
      pageSize: 7,
      search: '',
      sort: 'lastOrderDesc',
      status: 'all',
    });
    expect(fixture.nativeElement.textContent).toContain('Cliente Teste');
    expect(fixture.nativeElement.textContent).toContain('cliente@teste.com');
    expect(fixture.nativeElement.textContent).toContain('(11) 98888-7777');
    expect(fixture.nativeElement.textContent).toContain('R$ 92,50');
    expect(fixture.nativeElement.textContent).toContain('Ticket médio');
    expect(fixture.nativeElement.textContent).toContain('R$ 92,50');
    expect(fixture.nativeElement.querySelector('.filter-bar')).not.toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.filter-bar > *')).toHaveLength(3);
    expect(fixture.nativeElement.textContent).toContain('Último pedido');
    expect(fixture.nativeElement.textContent).toContain('Ações');
  });

  it('requests the selected page from the backend pagination', () => {
    orderServiceMock.getStoreCustomers
      .mockReturnValueOnce(of(buildResponse({ totalItems: 8, totalPages: 2 })))
      .mockReturnValueOnce(of(buildResponse({ page: 2, totalItems: 8, totalPages: 2 })));

    const fixture = TestBed.createComponent(SellerCustomersPageComponent);
    fixture.detectChanges();

    const pageTwoButton = Array.from(fixture.nativeElement.querySelectorAll('.pagination-btns button'))
      .find((button: HTMLButtonElement) => button.textContent?.trim() === '2') as HTMLButtonElement;

    pageTwoButton.click();
    fixture.detectChanges();

    expect(orderServiceMock.getStoreCustomers).toHaveBeenLastCalledWith({
      page: 2,
      pageSize: 7,
      search: '',
      sort: 'lastOrderDesc',
      status: 'all',
    });
    expect(fixture.nativeElement.textContent).toContain('R$');
  });

  it('renders edit and activate actions for customers', () => {
    orderServiceMock.getStoreCustomers.mockReturnValue(of(buildResponse()));

    const fixture = TestBed.createComponent(SellerCustomersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[aria-label^="Editar cliente"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-label^="Inativar cliente"]')).not.toBeNull();
  });

  it('opens the edit modal and saves customer information through the backend', () => {
    const customer = buildResponse().items[0];
    orderServiceMock.getStoreCustomers.mockReturnValue(of(buildResponse()));
    orderServiceMock.updateStoreCustomer.mockReturnValue(of({ ...customer, name: 'Cliente Editado' }));
    const fixture = TestBed.createComponent(SellerCustomersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.editCustomer(customer);
    fixture.componentInstance.editName.set('Cliente Editado');
    fixture.componentInstance.saveCustomer();

    expect(orderServiceMock.updateStoreCustomer).toHaveBeenCalledWith('customer1', {
      name: 'Cliente Editado',
      email: customer.email,
      phone: customer.phone,
      cep: '',
      street: '',
      number: '',
      complement: '',
      neighborhood: '',
      city: '',
      state: '',
    });
    expect(fixture.componentInstance.editingCustomer()).toBeNull();
  });

  it('toggles customer active status through the backend', () => {
    const customer = buildResponse().items[0];
    orderServiceMock.getStoreCustomers.mockReturnValue(of(buildResponse()));
    orderServiceMock.toggleStoreCustomer.mockReturnValue(of({ ...customer, isActive: false }));
    const fixture = TestBed.createComponent(SellerCustomersPageComponent);
    fixture.detectChanges();

    fixture.componentInstance.toggleCustomer(customer);

    expect(orderServiceMock.toggleStoreCustomer).toHaveBeenCalledWith('customer1', false);
    expect(fixture.componentInstance.customers()[0].isActive).toBe(false);
  });

  it('renders customers inside the shared seller table shell', () => {
    orderServiceMock.getStoreCustomers.mockReturnValue(of(buildResponse({
      totalItems: 8,
      totalPages: 2,
      metrics: {
        totalCustomers: 8,
        activeCustomers: 5,
        recurringCustomers: 3,
        newCustomersThisMonth: 2,
        averageTicket: 92.5,
      },
    })));

    const fixture = TestBed.createComponent(SellerCustomersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.content-head')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.seller-table')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Gerencie seus clientes e acompanhe seus pedidos mais recentes.');
    expect(fixture.nativeElement.textContent).toContain('Mostrando 1 a 7 de 8 clientes');
  });

  it('renders empty state when there are no customers yet', () => {
    orderServiceMock.getStoreCustomers.mockReturnValue(of(buildResponse({ totalItems: 0, totalPages: 0, metrics: {
      totalCustomers: 0,
      activeCustomers: 0,
      recurringCustomers: 0,
      newCustomersThisMonth: 0,
      averageTicket: 0,
    }, items: [] })));

    const fixture = TestBed.createComponent(SellerCustomersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Nenhum cliente encontrado.');
    expect(fixture.nativeElement.querySelector('.seller-state-card.is-empty')).not.toBeNull();
  });

  it('shows retry state when customers fail to load', () => {
    orderServiceMock.getStoreCustomers.mockReturnValue(throwError(() => new Error('network')));

    const fixture = TestBed.createComponent(SellerCustomersPageComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Erro ao carregar clientes');
    expect(fixture.nativeElement.querySelector('.seller-state-card.is-error button').textContent).toContain('Tentar novamente');
  });

  it('formats customer phone numbers and empty last orders for display', () => {
    const fixture = TestBed.createComponent(SellerCustomersPageComponent);
    const component = fixture.componentInstance;

    expect(component.formatPhone('11988887777')).toBe('(11) 98888-7777');
    expect(component.formatPhone('1188887777')).toBe('(11) 8888-7777');
    expect(component.formatDate(null)).toBe('-');
  });

});
