(async () => {
  const pages=['overview','downloads','localization','themes','diagnostics'];
  const results=[];
  for(const page of pages){
    document.querySelector(`nav [data-page="${page}"]`).click();
    await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
    const width=document.documentElement.clientWidth;
    const scrollWidth=document.documentElement.scrollWidth;
    const overflow=Array.from(document.querySelectorAll('main *')).filter(el=>{
      const rect=el.getBoundingClientRect();
      return rect.width>0&&(rect.right>width+1||rect.left< -1);
    }).map(el=>el.className||el.tagName);
    const sponsor=document.querySelector('.sidebar .sponsor');
    const sponsorRect=sponsor.getBoundingClientRect();
    const sponsorVisible=sponsorRect.width>0&&sponsorRect.height>0;
    results.push({page,width,scrollWidth,overflow,sponsorVisible,passed:scrollWidth<=width&&overflow.length===0&&sponsorVisible});
  }
  const reduced=matchMedia('(prefers-reduced-motion: reduce)').matches;
  return {viewport:[innerWidth,innerHeight],reducedMotion:reduced,transition:getComputedStyle(document.querySelector('#toast')).transitionDuration,results};
})()
