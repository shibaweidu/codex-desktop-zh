// Conservative token/material samples, NOT a full pixel-level WCAG audit.
// axe cannot determine the background behind text over translucent gradients.
(() => {
  const styles=getComputedStyle(document.documentElement);
  const read=name=>styles.getPropertyValue(`--${name}`).trim();
  const rgb=hex=>hex.replace('#','').match(/../g).map(x=>parseInt(x,16));
  const overlayWhite=(base,alpha)=>base.map(c=>c+(255-c)*alpha);
  const luminance=rgb=>rgb.map(c=>c/255).map(c=>c<=.04045?c/12.92:Math.pow((c+.055)/1.055,2.4)).reduce((sum,c,i)=>sum+c*[.2126,.7152,.0722][i],0);
  const ratio=(fg,bg)=>{const values=[luminance(fg),luminance(bg)].sort((a,b)=>b-a);return (values[0]+.05)/(values[1]+.05);};
  const light=document.documentElement.dataset.toolTheme==='paper';
  const samples=[
    ['正文 / 内容背景','text','bg',.008],
    ['辅助文字 / 侧栏顶光','muted','sidebar',.075],
    ['辅助文字 / 面板顶光','muted','surface',light?.45:.075],
    ['选中导航 / 淡紫材质','accent','accent-soft',.035],
    ['普通按钮 / 顶光','text','raised',light?.45:.075],
    ['主按钮 / 强调色','accent-ink','accent',0]
  ];
  const results=samples.map(([sample,foreground,background,alpha])=>{
    const foregroundColor=read(foreground),baseBackground=read(background);
    const value=ratio(rgb(foregroundColor),overlayWhite(rgb(baseBackground),alpha));
    return {sample,foreground:foregroundColor,baseBackground,whiteOverlay:alpha,contrast:Number(value.toFixed(2)),passed:value>=4.5};
  });
  return {palette:document.documentElement.dataset.toolTheme,scope:'Token/material samples only; actual gradient text still requires visual review',results,passed:results.every(x=>x.passed)};
})()
