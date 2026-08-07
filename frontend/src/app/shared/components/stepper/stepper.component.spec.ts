import { StepperComponent, StepperItem } from './stepper.component';

describe('StepperComponent', () => {
  let component: StepperComponent;

  beforeEach(() => {
    component = new StepperComponent();
  });

  describe('computedProgress', () => {
    it('should return 0 when steps array is empty', () => {
      component.steps = [];
      expect(component.computedProgress).toBe(0);
    });

    it('should return 0 when on first step', () => {
      component.steps = [
        { label: 'Loja', isActive: true, isCompleted: false },
        { label: 'Horários', isActive: false, isCompleted: false },
      ] as StepperItem[];
      expect(component.computedProgress).toBe(0);
    });

    it('should return 50 when on step 3 of 5 (index 2)', () => {
      component.steps = [
        { label: 'Loja', isActive: false, isCompleted: true },
        { label: 'Horários', isActive: false, isCompleted: true },
        { label: 'Entrega', isActive: true, isCompleted: false },
        { label: 'Produtos', isActive: false, isCompleted: false },
        { label: 'Publicar', isActive: false, isCompleted: false },
      ] as StepperItem[];
      // active index = 2, total = 5 → (2/5)*100 = 40
      expect(component.computedProgress).toBe(40);
    });

    it('should return 100 when all steps completed', () => {
      component.steps = [
        { label: 'Loja', isActive: false, isCompleted: true },
        { label: 'Horários', isActive: false, isCompleted: true },
        { label: 'Entrega', isActive: false, isCompleted: true },
        { label: 'Produtos', isActive: false, isCompleted: true },
        { label: 'Publicar', isActive: true, isCompleted: false },
      ] as StepperItem[];
      expect(component.computedProgress).toBe(80);
    });

    it('should return 20 when on step 2 of 5', () => {
      component.steps = [
        { label: 'Loja', isActive: false, isCompleted: true },
        { label: 'Horários', isActive: true, isCompleted: false },
        { label: 'Entrega', isActive: false, isCompleted: false },
        { label: 'Produtos', isActive: false, isCompleted: false },
        { label: 'Publicar', isActive: false, isCompleted: false },
      ] as StepperItem[];
      expect(component.computedProgress).toBe(20);
    });

    it('should fallback to completed count when no step is active', () => {
      component.steps = [
        { label: 'Loja', isActive: false, isCompleted: true },
        { label: 'Horários', isActive: false, isCompleted: true },
        { label: 'Entrega', isActive: false, isCompleted: false },
      ] as StepperItem[];
      // no active step, 2/3 completed → 67 rounded
      expect(component.computedProgress).toBe(67);
    });
  });

  describe('onStepClick', () => {
    it('should emit stepClick when step is completed and has route', () => {
      const step: StepperItem = { label: 'Loja', isCompleted: true, isActive: false, route: '/loja' };
      const spy = jest.fn();
      component.stepClick.subscribe(spy);
      component.onStepClick(step);
      expect(spy).toHaveBeenCalledWith(step);
    });

    it('should not emit when step is not completed', () => {
      const step: StepperItem = { label: 'Horários', isCompleted: false, isActive: true, route: '/horarios' };
      const spy = jest.fn();
      component.stepClick.subscribe(spy);
      component.onStepClick(step);
      expect(spy).not.toHaveBeenCalled();
    });

    it('should not emit when step has no route', () => {
      const step: StepperItem = { label: 'Loja', isCompleted: true, isActive: false };
      const spy = jest.fn();
      component.stepClick.subscribe(spy);
      component.onStepClick(step);
      expect(spy).not.toHaveBeenCalled();
    });
  });
});
