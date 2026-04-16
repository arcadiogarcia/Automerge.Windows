const fs = require('fs');
const content = fs.readFileSync('d:\\Automerge.Windows\\.github\\workflows\\release.yml', 'utf8');
const lines = content.split('\n');
let issues = 0;

// Check for tabs  
lines.forEach((l, i) => {
  if (l.includes('\t')) {
    console.log('TAB at line', i+1, JSON.stringify(l.slice(0, 80)));
    issues++;
  }
});

// Check for BOM
if (content.charCodeAt(0) === 0xFEFF) {
  console.log('BOM detected at start of file!');
  issues++;
}

// Check for non-ASCII
let nonAscii = 0;
for (let i = 0; i < content.length; i++) {
  if (content.charCodeAt(i) > 127) nonAscii++;
}

console.log('Non-ASCII chars:', nonAscii);
console.log('Has BOM:', content.charCodeAt(0) === 0xFEFF);
console.log('Total issues:', issues);
console.log('Total lines:', lines.length);
console.log('File size (bytes):', Buffer.byteLength(content, 'utf8'));
