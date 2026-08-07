import { StepperItem } from '../components/stepper/stepper.component';

export const STORE_CONFIG_STEPS: { label: string; route: string }[] = [
  { label: 'Loja', route: '/configurar-loja' },
  { label: 'Horários', route: '/configurar-loja/horarios' },
  { label: 'Entrega', route: '/configurar-loja/entrega' },
  { label: 'Cardápio', route: '/configurar-loja/produtos' },
  { label: 'Publicar', route: '/configurar-loja/publicar' },
];

export function createStepperSteps(activeIndex: number): StepperItem[] {
  return STORE_CONFIG_STEPS.map((step, index) => ({
    ...step,
    isActive: index === activeIndex,
    isCompleted: index < activeIndex,
  }));
}
