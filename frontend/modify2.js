const fs = require('fs');
let htmlPath = 'Documentacao/FrontEnd/Landpage/index.html';
let destPath = 'frontend/src/app/features/landing-page/landing-page.component.html';
console.log('Source exists?', fs.existsSync(htmlPath));
console.log('Dest exists?', fs.existsSync(destPath));
let content = fs.readFileSync(htmlPath, 'utf8');
content = content.replace("Seu delivery, com&nbsp;cara\n          <br />\n          de <span class=\"accent\">restaurante</span>.", "{{ getContent('Hero', 'Title', 'Seu delivery, com&nbsp;cara\\n          <br />\\n          de <span class=\"accent\">restaurante</span>.') }}")
content = content.replace("Cardápio digital, pedidos online e painel de gestão num só sistema —\n          feito pra quem ainda atende cada cliente pelo nome.", "{{ getContent('Hero', 'Subtitle', 'Cardápio digital, pedidos online e painel de gestão num só sistema —\\n          feito pra quem ainda atende cada cliente pelo nome.') }}")
content = content.replace(/href="#" class="btn btn-ghost">Entrar<\/a>/g, 'href="#" class="btn btn-ghost" (click)="navigateToSellerLogin()">Entrar</a>')
content = content.replace(/href="#planos" class="btn btn-coral">Criar meu cardápio/g, 'href="#" class="btn btn-coral" (click)="navigateToSellerRegister()">Criar meu cardápio')
content = content.replace(/href="#planos" class="btn btn-primary">/g, 'href="#" class="btn btn-primary" (click)="navigateToSellerRegister()">')
content = content.replace(/href="#planos" class="btn btn-primary btn-block">/g, 'href="#" class="btn btn-primary btn-block" (click)="navigateToSellerRegister()">')
content = content.replace(/[\s\S]*<body>\n/im, '');
content = content.replace(/<\/body>[\s\S]*/im, '');
fs.writeFileSync(destPath, content, 'utf8');
console.log('Done!');
