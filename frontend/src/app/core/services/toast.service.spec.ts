import { TestBed } from '@angular/core/testing';
import { ToastService } from './toast.service';

// Mock @ionic/angular/standalone
jest.mock('@ionic/angular/standalone', () => ({
  ToastController: jest.fn().mockImplementation(() => ({
    create: jest.fn(),
  })),
}));

const { ToastController } = require('@ionic/angular/standalone');

function makeToast() {
  return {
    present: jest.fn().mockResolvedValue(undefined),
    dismiss: jest.fn().mockResolvedValue(undefined),
    // Em produção só resolve quando o toast é realmente fechado; aqui fica pendente.
    onDidDismiss: jest.fn().mockReturnValue(new Promise(() => {})),
  };
}

describe('ToastService', () => {
  let service: ToastService;
  let toastControllerMock: any;

  beforeEach(() => {
    toastControllerMock = {
      create: jest.fn().mockImplementation(() => Promise.resolve(makeToast())),
    };

    TestBed.configureTestingModule({
      providers: [
        ToastService,
        { provide: ToastController, useValue: toastControllerMock },
      ],
    });

    service = TestBed.inject(ToastService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should call create and present on showError', async () => {
    await service.showError('Test error message');

    expect(toastControllerMock.create).toHaveBeenCalledWith(
      expect.objectContaining({
        message: 'Test error message',
        cssClass: 'urbeat-toast urbeat-toast-error',
        duration: 4000,
        position: 'top',
      }),
    );
  });

  it('should call create and present on showSuccess', async () => {
    await service.showSuccess('Test success message');

    expect(toastControllerMock.create).toHaveBeenCalledWith(
      expect.objectContaining({
        message: 'Test success message',
        cssClass: 'urbeat-toast urbeat-toast-success',
        duration: 4000,
        position: 'top',
      }),
    );
  });

  it('should dismiss the previous toast before showing a new one', async () => {
    const firstToast = makeToast();
    toastControllerMock.create = jest
      .fn()
      .mockResolvedValueOnce(firstToast)
      .mockResolvedValue(makeToast());

    await service.showError('first');
    await service.showError('second');

    expect(firstToast.dismiss).toHaveBeenCalled();
  });

  it('showGrouped should combine lines into a single message with severity color and 20s duration', async () => {
    await service.showGrouped([
      { type: 'error', text: 'Erro A' },
      { type: 'warning', text: 'Aviso B' },
    ]);

    const arg = toastControllerMock.create.mock.calls[0][0];
    expect(arg.message).toContain('Erro A');
    expect(arg.message).toContain('Aviso B');
    expect(arg.message).toContain('\n');
    expect(arg.duration).toBe(20000);
    // severidade máxima = error
    expect(arg.cssClass).toContain('urbeat-toast-error');
    expect(arg.cssClass).toContain('urbeat-toast-grouped');
  });

  it('showGrouped should ignore empty lines and not create a toast when all are empty', async () => {
    await service.showGrouped([{ type: 'error', text: '   ' }]);
    expect(toastControllerMock.create).not.toHaveBeenCalled();
  });
});
