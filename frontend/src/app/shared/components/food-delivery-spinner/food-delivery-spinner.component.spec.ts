import { TestBed } from '@angular/core/testing';
import { readFileSync } from 'fs';
import { resolve } from 'path';
import { FoodDeliverySpinnerComponent } from './food-delivery-spinner.component';

function componentSource(): string {
  return readFileSync(resolve(__dirname, 'food-delivery-spinner.component.ts'), 'utf8');
}

function ruleBlock(selector: string): string {
  const match = componentSource().match(new RegExp(`${selector.replace('.', '\\.')}\\s*\\{([\\s\\S]*?)\\}`));
  return match ? match[1] : '';
}

describe('FoodDeliverySpinnerComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FoodDeliverySpinnerComponent],
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('renders the burger as an inline SVG vector, never a raster image', () => {
    const fixture = TestBed.createComponent(FoodDeliverySpinnerComponent);
    fixture.detectChanges();

    const svg = fixture.nativeElement.querySelector('.delivery-rider-scene svg') as SVGElement | null;

    expect(svg).not.toBeNull();
    expect(svg?.getAttribute('viewBox')).toBe('0 0 280 150');
    expect(svg?.getAttribute('preserveAspectRatio')).toBe('xMidYMid meet');
    expect(fixture.nativeElement.querySelector('img')).toBeNull();
  });

  it('scales the scene proportionally with clamp/min dimensions and a locked aspect ratio', () => {
    const scene = ruleBlock('.delivery-rider-scene');

    expect(scene).toMatch(/width:\s*clamp\(/);
    expect(scene).toMatch(/max-width:\s*100%/);
    expect(scene).toMatch(/aspect-ratio:\s*280\s*\/\s*150/);
    expect(componentSource()).toMatch(/svg\s*\{[\s\S]*width:\s*100%[\s\S]*height:\s*100%/);
  });

  it('derives the scene size from viewport width and height so short screens shrink it', () => {
    const scene = ruleBlock('.delivery-rider-scene');
    const widthDeclaration = scene.match(/width:\s*([^;]+);/);

    expect(widthDeclaration).not.toBeNull();
    expect(widthDeclaration![1]).toMatch(/clamp\(/);
    expect(widthDeclaration![1]).toMatch(/min\(/);
    expect(widthDeclaration![1]).toMatch(/vw/);
    expect(widthDeclaration![1]).toMatch(/vh/);
  });

  it('caps the scene size so it does not dominate larger tablet and desktop viewports', () => {
    const scene = ruleBlock('.delivery-rider-scene');
    const widthDeclaration = scene.match(/width:\s*([^;]+);/);

    expect(widthDeclaration).not.toBeNull();
    expect(widthDeclaration![1]).toMatch(/,\s*[\d.]+(px|rem)\s*\)$/);
  });

  it('adds a short-height media query that scales the scene down further', () => {
    const source = componentSource();

    expect(source).toMatch(
      /@media\s*\(max-height:\s*\d+px\)\s*\{[\s\S]*\.delivery-rider-scene\s*\{[\s\S]*width:\s*clamp\(/
    );
  });

  it('assembles the burger layers with staggered motion and respects reduced motion', () => {
    const styles = componentSource();

    expect(styles).toContain('@keyframes assemble-burger');
    expect(styles).toMatch(/\.burger-layer\s*\{[\s\S]*animation:\s*assemble-burger/);
    expect(styles).toMatch(/\.burger-lettuce\s*\{[\s\S]*animation-delay:/);
    expect(styles).toMatch(/prefers-reduced-motion:\s*reduce/);
    expect(styles).toMatch(/prefers-reduced-motion:\s*reduce[\s\S]*animation:\s*none/);
  });
});
