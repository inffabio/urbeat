import re

with open(r'c:\Projetos\urbeat\frontend\src\app\core\icons.ts', 'r', encoding='utf-8') as f:
    text = f.read()

text = text.replace("'restaurant': restaurant,", "")
text = text.replace("'time': time,", "")
text = text.replace("'star': star,", "")
text = text.replace("'arrow-forward': arrowForward,", "")
text = text.replace("'log-in': logIn,", "")
text = text.replace("'person-add': personAdd,", "")

with open(r'c:\Projetos\urbeat\frontend\src\app\core\icons.ts', 'w', encoding='utf-8') as f:
    f.write(text)
