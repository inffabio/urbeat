import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ProductCardComponent } from './product-card.component';
import { Product } from '../../models/product.model';

describe('ProductCardComponent', () => {
  let fixture: ComponentFixture<ProductCardComponent>;
  let component: ProductCardComponent;

  const simpleProduct: Product = {
    id: 'p1',
    storeId: 's1',
    categoryId: 'c1',
    categoryName: 'Lanches',
    name: 'Burger artesanal',
    description: 'Pao, carne e queijo',
    price: 22,
    isAvailable: true,
    isFeatured: false,
    displayOrder: 1,
    saleMode: 'single',
    additionals: [],
    choiceOptions: [],
    variations: [],
    optionGroups: [],
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProductCardComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ProductCardComponent);
    component = fixture.componentInstance;
    component.product = simpleProduct;
  });

  it('opens the product instead of adding it when the plus affordance is clicked', () => {
    const openSpy = jest.spyOn(component.open, 'emit');
    const addSpy = jest.spyOn(component.add, 'emit');

    fixture.detectChanges();
    const plus = fixture.nativeElement.querySelector('.add-btn') as HTMLButtonElement;
    plus.click();

    expect(openSpy).toHaveBeenCalledTimes(1);
    expect(addSpy).not.toHaveBeenCalled();
  });
});
