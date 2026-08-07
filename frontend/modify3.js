const fs = require('fs');
let htmlPath = 'Documentacao/FrontEnd/Landpage/index.html';
let destPath = 'frontend/src/app/features/landing-page/landing-page.component.html';
let content = fs.readFileSync(htmlPath, 'utf8');

// remove head/body tags
content = content.replace(/[\s\S]*<body>\n/im, '');
content = content.replace(/<\/body>[\s\S]*/im, '');

// just inject the router links
content = content.replace(/href="#" class="btn btn-ghost">Entrar<\/a>/g, 'href="javascript:void(0)" class="btn btn-ghost" (click)="navigateToSellerLogin()">Entrar</a>')
content = content.replace(/href="#planos" class="btn btn-coral">Criar meu cardápio/g, 'href="javascript:void(0)" class="btn btn-coral" (click)="navigateToSellerRegister()">Criar meu cardápio')
content = content.replace(/href="#planos" class="btn btn-primary">/g, 'href="javascript:void(0)" class="btn btn-primary" (click)="navigateToSellerRegister()">')
content = content.replace(/href="#planos" class="btn btn-primary btn-block">/g, 'href="javascript:void(0)" class="btn btn-primary btn-block" (click)="navigateToSellerRegister()">')

fs.writeFileSync(destPath, content, 'utf8');
