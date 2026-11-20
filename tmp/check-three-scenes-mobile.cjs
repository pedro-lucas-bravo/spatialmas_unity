const assert = require('node:assert/strict');
const { chromium } = require('C:/Program Files/WindowsApps/OpenAI.CodexPrimaryRuntime.v26-1119-907-0_26.1119.907.0_x64__3k8sg7r9htsxt/dependencies/node/node_modules/playwright');
(async () => {
  const browser = await chromium.launch({channel:'msedge', headless:true});
  try {
    for (const width of [390]) {
      const mobile = width === 390;
      const page = await browser.newPage({viewport:{width,height:900},hasTouch:mobile,isMobile:mobile,reducedMotion:'reduce'});
      const errors = [];
      page.on('pageerror',err=>errors.push(err.message));
      page.setDefaultTimeout(6500);
      await page.goto('http://127.0.0.1:4321/index.html?review=3',{waitUntil:'domcontentloaded'});
      const stage = page.locator('#encounter-stage');
      async function select(key) {
        await page.locator('[data-scene="'+key+'"]').click();
        assert.equal(await page.locator('[data-scene="'+key+'"]').getAttribute('aria-pressed'),'true');
      }
      const note = page.locator('#encounter-status');
      await page.getByRole('button',{name:'Try a reach'}).click();
      await page.waitForFunction(()=>document.querySelector('#encounter-status').textContent.includes('steer'));
      await page.getByRole('button',{name:'Withdraw hand'}).click();
      await select('music');
      await stage.scrollIntoViewIfNeeded();
      await page.waitForFunction(()=>document.querySelector('#encounter-status').textContent.includes('red cube'));
      const box = await stage.boundingBox();
      const scale = Math.min(box.width/520,box.height/334);
      const project=(x,z)=>({x:box.x+(box.width-520*scale)/2+(260+217*x)*scale,y:box.y+(box.height-334*scale)/2+(136-84*z)*scale});
      const a=project(-.45,-.50),b=project(.43,-.4);
      if (mobile) {
        const client=await page.context().newCDPSession(page);
        await client.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:a.x,y:a.y}]});
        await client.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:b.x,y:b.y}]});
        await page.waitForFunction(()=>document.querySelector('#encounter-status').textContent.includes('yellow'));
        await client.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
      } else {
        await page.mouse.move(a.x,a.y);await page.mouse.down();await page.mouse.move(b.x,b.y,{steps:8});
        await page.waitForFunction(()=>document.querySelector('#encounter-status').textContent.includes('yellow'));
        await page.mouse.up();
      }
      await page.getByRole('button',{name:'Sound off'}).click();
      assert.equal(await page.getByRole('button',{name:'Sound on'}).getAttribute('aria-pressed'),'true');
      await select('swarm');
      await stage.focus();await page.keyboard.press('Space');await page.keyboard.press('ArrowUp');
      await page.waitForFunction(()=>document.querySelector('#encounter-status').textContent.includes('with your hand'));
      await page.keyboard.press('Space');
      await page.waitForFunction(()=>document.querySelector('#encounter-status').textContent.includes('again'));
      await page.getByRole('button',{name:'Move a cube'}).click();
      await page.getByRole('button',{name:'Move it back'}).click();
      await page.getByRole('button',{name:'Sound on'}).click();
      await page.getByRole('button',{name:'Resume motion'}).click();
      await select('crowd');
      await page.getByRole('button',{name:'Try a reach'}).click();
      await page.waitForFunction(()=>document.querySelector('#encounter-status').textContent.includes('steer'));
      if (!mobile) {
        await page.evaluate(()=>window.scrollTo({top:0,behavior:'instant'}));
        await page.screenshot({path:'tmp/website-sketch/three-scenes-preview.png',timeout:16000});
      }
      await page.locator('[data-open-scene="music"]').click();
      assert.equal(await page.locator('[data-scene="music"]').getAttribute('aria-pressed'),'true');
      await page.evaluate(()=>{
        window.observations=[];
        for(const type of ['pointerdown','pointermove','pointerup','click']) document.addEventListener(type,e=>window.observations.push([e.type,e.target.id,e.target.textContent.slice(0,25),e.clientX,e.clientY]),true);
      });
      await page.getByRole('button',{name:'Bring cubes together'}).click();
      await page.waitForTimeout(750);
      console.log(await page.evaluate(()=>({events:window.observations,offset:window.scrollY,rect:document.querySelector('#scene-examples').getBoundingClientRect().toJSON(),action:document.querySelector('#encounter-reach').textContent,caption:document.querySelector('#encounter-status').textContent})));
      await page.waitForFunction(()=>document.querySelector('#encounter-status').textContent.includes('yellow'));
      if (mobile) await page.locator('#scene-examples').screenshot({path:'tmp/website-sketch/three-scenes-mobile.png',timeout:16000});
      assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
      assert.deepEqual(errors,[]);
      console.log('Passed '+width+'px: selector, scene shortcut, reach/withdraw, cube drag, proximity response, keyboard grab/release, sound toggle, motion controls and horizontal fit.');
      await page.close();
    }
  } finally { await browser.close(); }
})().catch(err=>{console.error(err);process.exitCode=1});

