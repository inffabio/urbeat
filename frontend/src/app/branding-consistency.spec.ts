import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

describe('brand casing', () => {
  it('uses lowercase urbeat in user-facing frontend and landing-page sources', () => {
    const files = [
      'src/app/features/landing-page/landing-page.component.html',
      'src/app/features/landing-page/landing-page.component.ts',
      'src/app/features/landing-page/landing-page.component.scss',
      'src/theme/global.scss',
      'src/theme/variables.scss',
      'src/theme/onboarding-green.scss',
      'src/app/shared/components/wizard-header/wizard-header.component.ts',
      'src/app/features/forgot-password/reset-password-page.component.ts',
      'src/app/features/forgot-password/forgot-password-page.component.ts',
      'src/app/features/seller-login/seller-login-page.component.html',
      'src/app/core/services/install-prompt.service.ts',
      'src/app/features/seller-install/seller-install-page.component.ts',
      'src/app/features/seller-install/seller-install-page.component.html',
      'src/app/features/seller-install/seller-install-page.component.spec.ts',
      'src/app/features/seller-register/seller-register-page.component.html',
      'capacitor.config.ts',
      'android/app/src/main/res/values/strings.xml',
      'ios/App/App/Info.plist',
      'ios/App/App/capacitor.config.json',
      'android/app/src/main/assets/capacitor.config.json',
      'src/app/features/email-confirm/email-confirm-page.component.html',
      'src/app/features/email-confirmation/email-confirmation-page.component.html',
      'src/app/features/seller-printing/bluetooth-printer.adapter.ts',
      'src/app/features/seller-printing/adapters/wifi-escpos.adapter.ts',
      'src/app/features/seller-printing/adapters/escpos-bluetooth.adapter.ts',
      'src/app/features/seller-printing/adapters/browser-print.adapter.ts',
      'src/manifest.webmanifest',
      '../Documentacao/FrontEnd/Urbeat-Landpage/index.html',
      '../Documentacao/FrontEnd/Urbeat-Landpage/README.md',
      '../Documentacao/FrontEnd/Urbeat-Landpage/assets/README.md',
    ];

    for (const file of files) {
      const content = readFileSync(resolve(process.cwd(), file), 'utf8');
      const userFacingContent = content.replace(/Urbeat\.PrintAgent/g, '');
      expect(userFacingContent).not.toMatch(/UrBeat|Urbeat|URBEAT/);
    }
  });
});
