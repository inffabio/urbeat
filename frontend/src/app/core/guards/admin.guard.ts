import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth.service';
import { ToastController } from '@ionic/angular/standalone';
import { isAdmin } from '../utils/jwt.helper';

export const adminGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const toastCtrl = inject(ToastController);

  const token = auth.getToken();

  if (token && isAdmin(token)) {
    return true;
  }

  const toast = await toastCtrl.create({
    message: 'Acesso restrito. Faça login como administrador.',
    duration: 3000,
    color: 'warning',
    position: 'top',
  });
  await toast.present();

  // Se tiver token mas não for admin, faz logout e redireciona
  if (token) {
    auth.logout();
  }
  
  router.navigateByUrl('/painel/login');
  return false;
};
