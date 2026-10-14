'use strict';
const A='../', W=1600,H=900;
let scene;
function node(tag,cls,style,html=''){const e=document.createElement(tag);e.className=cls;e.style.cssText=style;e.innerHTML=html;scene.append(e);return e}
function art(path,x,y,w,h,extra={}){
 const b=ASSET_BOUNDS[path]||{width:128,height:128,bbox:[0,0,128,128]};const [l,t,r,bt]=b.bbox;let sx=w/(r-l),sy=h/(bt-t);if(!extra.stretch){sx=sy=Math.min(sx,sy);x+=(w-(r-l)*sx)/2;y+=(h-(bt-t)*sy)/2;}
 const e=node('div','layer',`left:${x}px;top:${y}px;width:${(r-l)*sx}px;height:${(bt-t)*sy}px;z-index:${extra.z||1};opacity:${extra.opacity??1};transform:rotate(${extra.rotate||0}deg);`);
 e.dataset.asset=path;
 if(extra.shadow){const sh=document.createElement('img');sh.className='contact-shadow';sh.src=A+'kitchen/shadows/'+extra.shadow+'.svg';sh.alt='';sh.style.cssText=`left:${-0.08*(r-l)*sx}px;top:${0.77*(bt-t)*sy}px;width:${1.2*(r-l)*sx}px;height:${0.3*(bt-t)*sy}px`;e.append(sh);}
 const im=document.createElement('img');im.src=A+path;im.alt='';im.style.cssText=`left:${-l*sx}px;top:${-t*sy}px;width:${b.width*sx}px;height:${b.height*sy}px`;e.append(im);return e;
}
function text(s,x,y,w=300,size=30,extra=''){return node('div','text',`left:${x}px;top:${y}px;width:${w}px;font-size:${size}px;${extra}`,s)}
function glyph(name,x,y,size=65,z=55){return art('ui/glyphs/'+name+'.svg',x,y,size,size,{z})}
function paper(x,y,w,h,z=42){return art('ui/panel-cream.png',x,y,w,h,{stretch:true,z})}
function button(name,x,y,w=112,h=82,target,base=true){if(base)art('ui/button-cream.png',x,y,w,h,{stretch:true,z:65});const e=node('button','control',`left:${x+20}px;top:${y+12}px;width:${w-40}px;height:${h-24}px;`);e.setAttribute('aria-label',target||name);e.innerHTML=`<img class="symbol" alt="" src="${A}ui/glyphs/${name}.svg">`;if(target)e.onclick=()=>show(target);return e}
function uiButton(file,x,y,w=110,h=100,target){const e=node('button','control',`left:${x}px;top:${y}px;width:${w}px;height:${h}px;`);e.setAttribute('aria-label',target||file);e.innerHTML=`<img class="symbol" alt="" src="${A}ui/${file}.png">`;if(target)e.onclick=()=>show(target);return e}
function hud(){
 paper(22,18,325,78,60);art('ui/day-sun.png',35,28,50,48,{z:62});text('6',91,37,40,30,'z-index:70');art('ui/clock-day.png',147,26,52,53,{z:62});text('14:35',215,37,115,30,'z-index:70');
 paper(1040,18,400,78,60);art('ui/coin-pancake.png',1058,29,56,51,{z:62});text('325',1125,37,90,31,'z-index:70');art('ui/currency-honey.png',1247,26,45,55,{z:62});text('8',1310,37,80,31,'z-index:70');button('pause',1462,18,112,78,'pause');
}
function stars(x,y,count=5,size=45){for(let i=0;i<5;i++)art('ui/rating-star.png',x+i*(size+10),y,size,size,{z:51,opacity:i<count?1:.24})}
function avatar(id,x,y){const path=`visitors/${id}-neutral.png`,b=ASSET_BOUNDS[path],[l,t,r,bt]=b.bbox,s=110/((bt-t)*.42);const av=node('div','avatar',`left:${x}px;top:${y}px`);const im=document.createElement('img');im.src=A+path;im.alt='';im.style.cssText=`width:${b.width*s}px;height:${b.height*s}px;left:${55-(l+r)/2*s}px;top:${-t*s-4}px`;av.append(im);return av}
function back(){button('arrow-left',25,795,115,79,'counter')}
function serviceBase(){art('decor/service_background.png',0,0,1600,900,{stretch:true});art('decor/chair_premium.png',1025,414,160,280);art('decor/table_premium.png',846,500,320,247);art('decor/stool_base.png',1350,580,150,180);art('decor/table_base.png',1270,440,270,230);art('decor/vase_flowers.png',1000,391,100,130);art('decor/lamp_brass.png',1140,-38,165,240);}
function kitchenBase(){
 // Fitted painted room: the stove and counters form one continuous backdrop.
 art('kitchen/kitchen-background-crepe-stove.png',0,0,1600,900,{stretch:true});
 art('kitchen/batter-pitcher-floral-v2.png',18,265,260,260,{z:4,shadow:'pitcher'});
 art('kitchen/pancake-ready.png',377,308,363,235,{stretch:true,z:6});
 art('kitchen/ready-tray.png',1140,200,395,139,{stretch:true,z:4,shadow:'tray'});
 art('kitchen/fold-triangle.png',1192,220,126,90,{z:5,shadow:'fold'});
 art('kitchen/plate.png',1103,351,472,291,{stretch:true,z:4,shadow:'plate'});
 art('kitchen/pancake-ready.png',1158,390,362,213,{stretch:true,z:5});
 art('kitchen/filling-jam.png',1240,440,200,107,{stretch:true,z:6});
 const locked=new Set((new URLSearchParams(location.search).get('locked')||'').split(','));
 const fill=[['jam',864,255,141,127],['honey',1015,255,116,130],['curd',882,403,140,119],['mushrooms',886,540,133,96],['butter',1029,526,112,105]];
 fill.forEach(([id,x,y,w,h])=>{if(locked.has(id))return;art(`kitchen/container-${id}.png`,x,y,w,h,{z:6,shadow:id});if(id==='butter'||id==='curd')art('ui/helper-badge.png',x+w-19,y+h-15,27,27,{z:30})});
 if(!locked.has('jam'))node('div','ring','left:860px;top:255px;width:149px;height:132px;border-width:3px;transform:rotate(-4deg)');
 // Proposed two-side cooking indicator; tutorial labels are separate.
 paper(370,151,330,62,40);text('1',390,166,24,22);art('kitchen/pancake-ready.png',423,165,49,34,{z:45});glyph('check',480,171,23);
 text('2',552,166,24,22,'opacity:.55');art('kitchen/pancake-undercooked.png',585,165,49,34,{z:45,opacity:.55});
 button('arrow-left',24,804,110,76,'counter');uiButton('recipe-book-closed',159,797,87,90,'recipes');
 paper(1271,805,203,75,60);art('kitchen/scraper.png',1285,811,69,61,{z:65});text('×3',1381,824,75,30,'z-index:70');
 paper(1495,805,87,75,60);art('kitchen/discard-bowl.png',1510,811,57,62,{z:65});
 hud();paper(402,18,591,78,60);art('ui/receipt-order.png',424,31,40,52,{z:63});text('1/2',489,37,90,30,'z-index:70');
 art('ui/patience-track.png',624,38,335,37,{stretch:true,z:63});node('div','meter','left:651px;top:49px;width:225px;height:12px;z-index:64');
}
const screens={
 facade:{title:'Фасад',note:'Вход в блинную. Деревянные рамы, пустая вывеска и одна кнопка начала смены.',draw(){art('decor/facade_base.png',0,0,1600,900,{stretch:true});button('play',698,744,205,100,'counter');uiButton('settings-speaker',1460,30,100,100,'pause')}},
 counter:{title:'Стойка',note:'Посетитель, открытая реплика заказа, принятие и уточнение. Мебель, посетитель и стойка — отдельные слои.',draw(){serviceBase();art('visitors/student-neutral.png',94,191,470,792);art('decor/service_counter.png',-30,723,1660,240,{stretch:true,z:20});art('decor/service_delivery.png',1065,748,350,100,{z:25,shadow:'tray'});art('decor/pitcher.png',1444,658,112,183,{z:25});art('ui/speech-bubble.png',620,145,825,285,{stretch:true,z:40});text('Мне два блина с вареньем.<br>Сложите треугольником,<br>пожалуйста.',737,199,600,37,'font-family:Serif');button('check',856,448,176,86,'kitchen');button('help',1065,448,176,86);art('ui/patience-track.png',747,560,580,53,{stretch:true,z:45});node('div','meter','left:785px;top:579px;width:400px');uiButton('recipe-book-closed',28,770,108,104,'recipes');button('arrow-right',1448,795,120,80,'kitchen');hud()}},
 kitchen:{title:'Кухня',note:'Блинная плита для жарки, кувшин с тестом и стол для начинки и складывания. Тени принадлежат предметам и скрываются вместе с ними.',draw:kitchenBase},
 recipes:{title:'Книга рецептов',note:'Формы и состав читаются по рисункам. Замок закрывает ещё не изученную форму, звезда отмечает фирменный рецепт.',draw(){serviceBase();node('div','dim','');art('ui/recipe-book-open.png',142,100,1320,716,{stretch:true,z:42});art('ui/tab-sage.png',565,82,150,64,{stretch:true,z:41});art('ui/tab-sage.png',817,82,150,64,{stretch:true,z:41});art('kitchen/fold-triangle.png',278,255,363,258,{z:48});art('kitchen/container-butter.png',290,576,130,118,{z:48});glyph('plus',436,602,45);art('kitchen/container-jam.png',505,576,130,118,{z:48});art('ui/rating-star.png',599,190,64,64,{z:49});art('kitchen/fold-envelope.png',910,267,340,230,{z:48});art('kitchen/container-mushrooms.png',897,576,130,118,{z:48});glyph('plus',1040,602,45);art('kitchen/container-curd.png',1115,576,130,118,{z:48});glyph('lock',1192,194,67);button('arrow-left',203,726,120,70,'kitchen');button('arrow-right',1282,726,120,70);button('close',1470,29,102,78,'kitchen')}},
 shop:{title:'Магазин',note:'Карточки повторно используют рисунки ингредиентов, помощников, улучшений и декора. Цены в макете демонстрационные.',draw(){serviceBase();node('div','dim','');paper(180,152,1240,634);const cards=[['ui/upgrade-speed.png',251,275,'120','plus'],['kitchen/container-mushrooms.png',546,275,'80','plus'],['ui/helper-badge.png',840,275,'250','lock'],['decor/lamp_brass.png',1135,275,'180','check']];cards.forEach(([f,x,y,price,state])=>{art('ui/card-frame.png',x-16,y-54,252,418,{stretch:true,z:44});art(f,x+20,y,180,220,{z:48});const priceLeft=x+110-(58+price.length*18.2)/2;art('ui/coin-pancake.png',priceLeft,571,40,40,{z:48});text(price,priceLeft+58,568,110,33);glyph(state,x+85,666,50)});for(let i=0;i<3;i++){art('ui/tab-sage.png',410+i*255,119,193,69,{stretch:true,z:43});art(['ui/upgrade-speed.png','ui/helper-badge.png','decor/vase_flowers.png'][i],482+i*255,123,54,58,{z:48})}hud();button('arrow-left',25,795,115,79,'counter');uiButton('reward-video',1450,730,115,125,'rewards')}},
 day:{title:'Итог дня',note:'Выручка, чаевые и расходы показаны отдельными строками. Внизу — прибыль и переход к следующей смене.',draw(){serviceBase();node('div','dim','');paper(335,118,930,693);art('ui/day-sun.png',651,152,70,70,{z:49});text('6',749,168,100,47);const rows=[['order-plate','+420'],['tip-coins','+65'],['refund-coins','−35'],['repair-stove','−250']];rows.forEach(([f,n],i)=>{art('ui/'+f+'.png',495,256+i*91,77,76,{z:49});text(n,900,270+i*91,200,39,'text-align:right');node('div','rule',`left:607px;top:${318+i*91}px;width:493px`)});art('ui/coin-pancake.png',561,669,92,86,{z:49});text('+200',727,675,266,58);uiButton('upgrade-visitors',202,696,100,110,'shop');button('arrow-right',1320,707,142,95,'facade');button('undo',39,795,112,78,'kitchen')}},
 reviews:{title:'Отзывы',note:'Аватары берутся из рисунков посетителей. Оценка и реакция гостя остаются связанными.',draw(){serviceBase();node('div','dim','');paper(290,134,1020,657);stars(663,166,5,50);text('4,8',985,167,150,43);const rows=[['student','5','Спасибо! Как дома.'],['office','4','Вкусно. Чуть быстрее — и идеально.'],['elder-woman','5','За творожными обязательно вернусь.']];rows.forEach(([id,r,t],i)=>{let y=285+i*152;avatar(id,368,y);stars(535,y+4,+r,31);text(t,535,y+54,630,28);if(i<2)node('div','rule',`left:535px;top:${y+127}px;width:630px`)});button('close',1357,138,112,82,'counter')}},
 pause:{title:'Пауза',note:'Продолжение, звук и выход обозначены простыми значками на общей кремовой панели.',draw(){kitchenBase();node('div','dim','z-index:90');paper(465,225,670,420,91);const oldScene=scene;['play','mute','exit'].forEach((g,i)=>{const e=button(g,548+i*182,384,139,114,g==='play'?'kitchen':g==='exit'?'facade':undefined);e.style.zIndex='95';e.previousElementSibling.style.zIndex='94'});text('Ⅱ',758,275,100,58,'z-index:95');scene=oldScene}},
 rewards:{title:'Награды',note:'Дополнительный экран: просмотр за награду, билет без просмотра и две валюты.',draw(){serviceBase();node('div','dim','');paper(330,161,940,604);art('ui/reward-video.png',455,240,260,250,{z:48});art('ui/reward-ticket.png',880,252,257,213,{z:48});art('ui/coin-pancake.png',481,546,88,82,{z:48});text('+50',599,554,160,49);art('ui/currency-honey.png',892,532,65,96,{z:48});text('1',1002,555,100,49);button('play',512,660,150,80,'counter');button('check',918,660,150,80,'counter');button('close',1340,172,112,78,'counter')}}
};
function draw(target,key){scene=target;target.innerHTML='';screens[key].draw();target.dataset.screen=key}
function fit(el){const p=el.parentElement;el.style.transform=`scale(${p.clientWidth/W})`}
const nav=document.getElementById('navigation');for(const [key,v]of Object.entries(screens)){const b=document.createElement('button');b.textContent=v.title;b.dataset.key=key;b.onclick=()=>show(key);nav.append(b)}
function show(key){if(!screens[key])key='kitchen';draw(document.getElementById('stage'),key);fit(document.getElementById('stage'));document.querySelectorAll('nav button').forEach(b=>b.setAttribute('aria-selected',String(b.dataset.key===key)));document.getElementById('screen-title').textContent=screens[key].title;document.getElementById('screen-note').textContent=screens[key].note;history.replaceState(null,'','#'+key)}
const contact=document.getElementById('contact');for(const [key,v]of Object.entries(screens)){const f=document.createElement('figure');f.className='thumb';const b=document.createElement('button');b.setAttribute('aria-label',v.title);b.onclick=()=>{show(key);document.getElementById('viewport').scrollIntoView({behavior:'smooth'})};const d=document.createElement('div');d.className='stage';b.append(d);f.append(b);const c=document.createElement('figcaption');c.textContent=v.title;f.append(c);contact.append(f);draw(d,key)}
const qs=new URLSearchParams(location.search);if(qs.has('capture'))document.body.classList.add('capture');if(qs.has('contact'))document.body.classList.add('contact-capture');
show(location.hash.slice(1)||'kitchen');const ro=new ResizeObserver(()=>document.querySelectorAll('.stage').forEach(fit));ro.observe(document.querySelector('main'));window.renderScreen=show;window.screenKeys=Object.keys(screens);


