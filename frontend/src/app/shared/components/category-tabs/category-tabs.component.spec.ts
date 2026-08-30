import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { CategoryTabsComponent } from './category-tabs.component';

describe('CategoryTabsComponent', () => {
  let fixture: ComponentFixture<CategoryTabsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CategoryTabsComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(CategoryTabsComponent);
    fixture.componentInstance.tabs = [
      { id: 'todos', name: 'Todos' },
      { id: 'lanches', name: 'Lanches' },
      { id: 'bebidas', name: 'Bebidas' },
    ];
    fixture.componentInstance.activeId = 'todos';
    fixture.detectChanges();
  });

  it('scrolls the selected tab horizontally without changing page scroll', () => {
    const tab = fixture.debugElement.queryAll(By.css('.tab'))[2].nativeElement as HTMLElement;
    const scrollTo = jest.fn();
    Object.defineProperty(tab, 'offsetLeft', { value: 120 });
    Object.defineProperty(tab, 'offsetWidth', { value: 80 });
    Object.defineProperty(tab.parentElement, 'clientWidth', { value: 200 });
    tab.parentElement!.scrollTo = scrollTo;

    tab.click();

    expect(scrollTo).toHaveBeenCalledWith({ left: 60, behavior: 'smooth' });
  });
});
