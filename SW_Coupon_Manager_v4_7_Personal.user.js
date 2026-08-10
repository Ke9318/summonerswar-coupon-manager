// ==UserScript==
// @name         Summoners War 쿠폰 매니저
// @namespace    local.sw.coupon
// @version      4.7.0
// @description  SWGT 자동 스캔 + GUI 계정관리 + 새 쿠폰 표시 + 계정별 사용기록 + 백그라운드 자동등록
// @match        http://*/*
// @match        https://*/*
// @run-at       document-start
// @homepageURL  https://github.com/Ke9318/summonerswar-coupon-manager
// @updateURL    https://raw.githubusercontent.com/Ke9318/summonerswar-coupon-manager/main/SW_Coupon_Manager.user.js
// @downloadURL  https://raw.githubusercontent.com/Ke9318/summonerswar-coupon-manager/main/SW_Coupon_Manager.user.js
// @grant        GM_getValue
// @grant        GM_setValue
// @grant        GM_xmlhttpRequest
// @grant        GM_openInTab
// @grant        GM_addValueChangeListener
// @connect      swgt.io
// ==/UserScript==

(() => {
'use strict';

const SOURCE_URL = 'https://swgt.io/gamecodes';
const COUPON_URL = 'https://event.withhive.com/ci/smon/evt_coupon';
const WORKER_URL = COUPON_URL + '?sw_coupon_worker=1';
const isCouponPage = () => location.href.startsWith(COUPON_URL);
const isWorker = new URLSearchParams(location.search).get('sw_coupon_worker') === '1';
const KEY = 'sw_coupon_manager_v46';

const DEFAULT_ACCOUNTS = [
  {id:'acc1', name:'저달처럼', hiveId:'jongeun2004', server:'korea'},
  {id:'acc2', name:'라시에', hiveId:'user0952c3b5', server:'korea'}
];

const fresh = () => ({
  accounts: DEFAULT_ACCOUNTS,
  selected: DEFAULT_ACCOUNTS.map(x => x.id),
  mode: 'new',
  running: false,
  waiting: false,
  scanCodes: [],
  newCodes: [],
  previousCodes: [],
  lastScanAt: '',
  queue: [],
  queueIndex: 0,
  current: null,
  sessionResults: [],
  history: {},              // history[accountId][code]
  knownCodes: [],
  ui: {collapsed:false, x:null, y:null},
  lastMessage: '',
  lastError: ''
});

let db = Object.assign(fresh(), GM_getValue(KEY, {}));
db.accounts = Array.isArray(db.accounts) ? db.accounts : DEFAULT_ACCOUNTS;
db.selected = Array.isArray(db.selected) ? db.selected : [];
db.history = db.history || {};
db.ui = Object.assign({collapsed:false,x:null,y:null}, db.ui || {});

const save = () => GM_setValue(KEY, db);
const reloadDB = () => { db = Object.assign(fresh(), GM_getValue(KEY, {})); db.ui = Object.assign({collapsed:false,x:null,y:null}, db.ui||{}); };

function esc(s){return String(s??'').replace(/[&<>"']/g,m=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m]));}
function norm(s){return String(s??'').replace(/\s+/g,' ').trim().toLowerCase();}
function visible(el){if(!el)return false;const r=el.getBoundingClientRect(),s=getComputedStyle(el);return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden';}
function uid(){return 'acc_'+Date.now().toString(36)+'_'+Math.random().toString(36).slice(2,7);}

function scanSWGT(){
  return new Promise((resolve,reject)=>{
    GM_xmlhttpRequest({
      method:'GET', url:SOURCE_URL+'?_='+Date.now(), timeout:15000,
      headers:{'Cache-Control':'no-cache'},
      onload:r=>{
        if(r.status<200||r.status>=400)return reject(new Error('SWGT HTTP '+r.status));
        try{
          const doc=new DOMParser().parseFromString(r.responseText,'text/html');
          const text=doc.body?.innerText||'';
          let codes=[...text.matchAll(/\bCode\s*:\s*([A-Z0-9]{5,40})\b/gi)].map(m=>m[1].toUpperCase());
          if(!codes.length){
            const bad=new Set(['SUMMONERS','ACTIVE','CODES','COMMUNITY','PROVIDED','ACCOUNT','MONSTERS','DASHBOARD','PASSWORD','USERNAME','DISCORD','REWARDS','PRIVACY','CONTACT','COOKIE']);
            codes=(text.toUpperCase().match(/\b[A-Z0-9]{7,32}\b/g)||[]).filter(x=>/[A-Z]/.test(x)&&/\d/.test(x)&&!bad.has(x)&&!/^\d+$/.test(x));
          }
          codes=[...new Set(codes)].filter(x=>/^[A-Z0-9]{5,40}$/.test(x));
          if(!codes.length) throw new Error('쿠폰 코드를 찾지 못했습니다.');
          resolve(codes);
        }catch(e){reject(e);}
      },
      onerror:()=>reject(new Error('SWGT 접속 실패')),
      ontimeout:()=>reject(new Error('SWGT 접속 시간 초과'))
    });
  });
}

async function doScan(){
  reloadDB();
  db.lastError='';
  db.lastMessage='SWGT 스캔 중...';
  save(); render();
  try{
    const codes=await scanSWGT();
    const prev=[...(db.scanCodes||[])];
    // "새 쿠폰"은 직전 스캔과 비교. 처음 설치한 경우 모든 현재 쿠폰을 NEW로 취급.
    const newly=codes.filter(c=>!prev.includes(c));
    db.previousCodes=prev;
    db.scanCodes=codes;
    db.newCodes=newly;
    db.lastScanAt=new Date().toLocaleString();
    db.knownCodes=[...new Set([...(db.knownCodes||[]),...codes])];
    db.lastMessage=newly.length ? `새 쿠폰 ${newly.length}개 발견!` : `새 쿠폰 없음 · 활성 ${codes.length}개`;
    save(); render();
    return codes;
  }catch(e){
    db.lastError=String(e?.message||e);
    db.lastMessage='스캔 실패';
    save(); render();
    throw e;
  }
}

function record(accountId,code,status,message){
  db.history[accountId] ||= {};
  db.history[accountId][code]={status,message,time:new Date().toLocaleString()};
}
function done(accountId,code){
  const r=db.history?.[accountId]?.[code];
  return !!r && ['success','already','expired','invalid'].includes(r.status);
}
function classify(msg){
  const m=norm(msg);
  if(/already|used|이미\s*사용|사용한|등록된/.test(m))return'already';
  if(/expired|만료/.test(m))return'expired';
  if(/success|complete|reward|성공|완료|보상|지급/.test(m))return'success';
  if(/invalid|not valid|유효하지|존재하지|wrong|잘못된|없는 쿠폰/.test(m))return'invalid';
  if(/error|오류|fail|실패|network|네트워크/.test(m))return'error';
  return'unknown';
}
function stat(s){return({success:'성공',already:'이미 사용',expired:'만료',invalid:'무효',error:'오류',unknown:'응답'})[s]||s;}

function buildQueue(){
  const selected=db.accounts.filter(a=>db.selected.includes(a.id));
  if(!selected.length)throw new Error('계정을 하나 이상 선택해 주세요.');

  // v4.7:
  // "새 쿠폰만"은 마지막 스캔에서 순간적으로 잡힌 newCodes가 아니라,
  // 현재 활성 쿠폰 중 선택 계정에서 아직 처리 완료 기록이 없는 쿠폰을 뜻한다.
  // 따라서 시작 버튼을 누르기 직전 재스캔해도 새 쿠폰이 사라지지 않는다.
  const codes=[...(db.scanCodes||[])];
  const q=[];

  for(const a of selected){
    for(const c of codes){
      if(db.mode==='all' || !done(a.id,c)){
        q.push({accountId:a.id,code:c});
      }
    }
  }
  return q;
}

function findSelect(){
  // 구형/예비 구조용 native select fallback
  const s=[...document.querySelectorAll('select')].filter(visible).filter(x=>!x.closest('#sw-v4'));
  return s.find(x=>[...x.options].some(o=>/korea|한국/i.test(o.textContent)))||s[0]||null;
}

async function chooseKorea(){
  // 현재 공식 쿠폰 교환소는 jQuery UI selectmenu를 사용:
  // <select id="EVTselect"> ... </select>
  // <span id="EVTselect-button" ...>게임 서버를 선택해 주세요.</span>

  const sel = document.querySelector('#EVTselect');

  if(sel){
    const options=[...sel.options];
    const ko=options.find(o=>{
      const t=norm(o.textContent);
      return t==='한국' || t==='한국 서버' || t==='korea' || t.includes('korea');
    });

    if(ko){
      sel.value=ko.value;

      // native 이벤트
      sel.dispatchEvent(new Event('input',{bubbles:true}));
      sel.dispatchEvent(new Event('change',{bubbles:true}));

      // jQuery / jQuery UI selectmenu 동기화
      try{
        const jq=window.jQuery || window.$;
        if(jq){
          const $sel=jq(sel);
          if(typeof $sel.selectmenu==='function'){
            try{$sel.selectmenu('refresh');}catch{}
          }
          try{$sel.trigger('change');}catch{}
          try{$sel.trigger('selectmenuchange');}catch{}
        }
      }catch{}

      await new Promise(r=>setTimeout(r,350));

      // 화면 표시까지 바뀌었는지 확인
      const btn=document.querySelector('#EVTselect-button');
      const shown=norm(btn?.textContent||'');
      if(shown.includes('한국') || shown.includes('korea') || sel.value===ko.value){
        return true;
      }
    }
  }

  // API 동기화가 실패할 때는 실제 selectmenu 버튼/메뉴를 클릭
  const opener=document.querySelector('#EVTselect-button');
  if(opener){
    try{opener.click();}catch{}

    for(let i=0;i<25;i++){
      await new Promise(r=>setTimeout(r,120));

      const menu =
        document.querySelector('#EVTselect-menu') ||
        document.querySelector('[id^="EVTselect"][id$="-menu"]');

      const choices=menu
        ? [...menu.querySelectorAll('li,div,a,span')]
        : [...document.querySelectorAll('.ui-menu-item,.ui-menu-item-wrapper,[role="option"]')];

      let item=choices.find(e=>{
        if(!visible(e)) return false;
        const t=norm(e.textContent);
        return t==='한국' || t==='한국 서버' || t==='korea' || t.includes('korea');
      });

      if(item){
        item=item.closest('.ui-menu-item-wrapper,.ui-menu-item,li,a,[role="option"]') || item;
        try{item.click();}catch{}
        await new Promise(r=>setTimeout(r,350));

        const btn=document.querySelector('#EVTselect-button');
        const shown=norm(btn?.textContent||'');
        if(shown.includes('한국') || shown.includes('korea')) return true;
      }
    }
  }

  return false;
}

function findInputs(){
  // 사용자 DevTools 캡처에서 확인된 실제 Hive ID 요소:
  // <input type="text" id="EVTid" placeholder="Hive ID를 입력해 주세요">
  const hive =
    document.querySelector('#EVTid') ||
    document.querySelector('input[placeholder*="Hive ID"]') ||
    document.querySelector('input[id*="id" i][placeholder*="Hive" i]');

  // 쿠폰 입력칸은 placeholder 텍스트 기준을 1순위로 사용
  const coupon =
    document.querySelector('#EVTcode') ||
    document.querySelector('input[placeholder*="쿠폰 코드"]') ||
    document.querySelector('input[placeholder*="Coupon" i]');

  return {hive,coupon};
}

function setVal(el,v){
  if(!el)return;
  const set=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value')?.set;
  set?set.call(el,v):(el.value=v);
  el.focus();
  el.dispatchEvent(new Event('input',{bubbles:true}));
  el.dispatchEvent(new Event('change',{bubbles:true}));
  el.dispatchEvent(new Event('blur',{bubbles:true}));
}

function findButton(){
  // 사용자 DevTools 캡처에서 확인:
  // <button type="button" class="btn_use" data-target="#EVTpop_coupon">쿠폰 사용</button>
  return document.querySelector('button.btn_use[data-target="#EVTpop_coupon"]') ||
         [...document.querySelectorAll('button,input[type="submit"],input[type="button"],a')]
           .filter(visible)
           .filter(e=>!e.closest('#sw-v4'))
           .find(e=>{
             const t=norm(e.textContent||e.value);
             return t==='쿠폰 사용' || t==='use coupon' || t==='redeem';
           }) || null;
}

function handleResult(message){
  reloadDB();
  if(!db.current)return;
  const a=db.accounts.find(x=>x.id===db.current.accountId);
  const status=classify(message);
  record(db.current.accountId,db.current.code,status,message);
  db.sessionResults.push({account:a?.name||'?',code:db.current.code,status,message,time:new Date().toLocaleTimeString()});
  db.waiting=false; db.current=null; db.queueIndex++;
  if(db.queueIndex>=db.queue.length){db.running=false;db.lastMessage='전체 작업 완료';}
  save();
}

function continueWorkerAfterResult(){
  reloadDB();
  if(!isWorker) return;

  if(!db.running || db.queueIndex >= db.queue.length){
    // GM_openInTab로 열린 탭은 window.close()가 허용되는 환경이 일반적.
    setTimeout(()=>{
      try{ window.close(); }catch{}
    },700);
  }else{
    setTimeout(()=>location.reload(),900);
  }
}

const nativeAlert=window.alert;
window.alert=function(msg){
  reloadDB();
  if(db.running&&db.waiting&&db.current&&isWorker){
    handleResult(String(msg||'결과 메시지 없음'));
    continueWorkerAfterResult();
    return;
  }
  return nativeAlert.call(window,msg);
};

function modalResult(){
  reloadDB();
  if(!db.running||!db.waiting||!db.current||!isWorker)return false;
  const boxes=[...document.querySelectorAll('[role="dialog"],.modal,.popup,.layer,.alert,.dimmed,.pop_wrap,.popup_wrap')].filter(visible);
  for(const b of boxes){
    const txt=(b.innerText||'').trim();
    if(!/coupon|쿠폰|used|사용|expired|만료|success|성공|invalid|유효|reward|보상|already|완료|error|오류|fail|실패/i.test(txt))continue;
    const btn=[...b.querySelectorAll('button,a,input[type="button"]')].find(visible);
    if(btn){
      handleResult(txt);
      try{btn.click()}catch{}
      continueWorkerAfterResult();
      return true;
    }
  }
  return false;
}

async function startRun(){
  reloadDB();
  try{
    await doScan();
    reloadDB();

    db.queue=buildQueue();
    db.queueIndex=0;
    db.sessionResults=[];
    db.current=null;
    db.waiting=false;

    if(!db.queue.length){
      db.running=false;
      db.lastMessage=db.mode==='new'
        ? '선택 계정에 새로 처리할 활성 쿠폰이 없습니다.'
        : '선택 조건에서 실행할 쿠폰이 없습니다.';
      save(); render(); return;
    }

    db.running=true;
    db.lastError='';
    db.lastMessage=`백그라운드 작업 시작 · ${db.queue.length}건`;
    save(); render();

    // 현재 탭은 그대로 유지하고 별도의 비활성 탭에서 쿠폰 등록.
    try{
      GM_openInTab(WORKER_URL, {
        active:false,
        insert:true,
        setParent:true
      });
    }catch(e){
      db.running=false;
      db.lastError='백그라운드 작업 탭을 열지 못했습니다: '+String(e?.message||e);
      save(); render();
    }
  }catch(e){
    reloadDB();
    db.running=false;
    db.lastError=String(e?.message||e);
    save(); render();
  }
}

async function runOne(){
  reloadDB(); render();
  if(!isWorker || !db.running || db.waiting)return;
  if(!isCouponPage()) return;
  if(db.queueIndex>=db.queue.length){
    db.running=false;db.lastMessage='전체 작업 완료';save();render();
    if(isWorker)setTimeout(()=>{try{window.close()}catch{}},500);
    return;
  }
  const item=db.queue[db.queueIndex], a=db.accounts.find(x=>x.id===item.accountId);
  if(!a){db.queueIndex++;save();return setTimeout(runOne,200);}
  // 공식 페이지의 폼이 늦게 생기는 경우 최대 약 5초 기다림
  let hive=null,coupon=null,btn=null;
  for(let i=0;i<25;i++){
    ({hive,coupon}=findInputs());
    btn=findButton();
    if(hive&&coupon&&btn)break;
    await new Promise(r=>setTimeout(r,200));
  }

  if(!hive||!coupon||!btn){
    db.running=false;
    db.lastError='공식 쿠폰 페이지의 Hive ID/쿠폰 코드/쿠폰 사용 버튼을 찾지 못했습니다.';
    save();render();return;
  }

  if(!(await chooseKorea())){
    db.running=false;
    db.lastError='게임 서버(#EVTselect)에서 한국 서버를 선택하지 못했습니다. 옵션 목록 구조가 예상과 다를 수 있습니다.';
    save();render();return;
  }

  setVal(hive,a.hiveId);
  setVal(coupon,item.code);
  db.waiting=true;db.current=item;db.lastMessage=`${a.name} / ${item.code} 제출 중`;save();render();
  const started=Date.now(); setTimeout(()=>btn.click(),900);
  const ob=new MutationObserver(()=>{reloadDB();if(!db.running||!db.waiting)return ob.disconnect();if(modalResult())ob.disconnect();});
  ob.observe(document.documentElement,{childList:true,subtree:true,attributes:true});
  setTimeout(()=>{reloadDB();if(db.running&&db.waiting&&Date.now()-started>18000){db.running=false;db.waiting=false;db.lastError='결과창을 인식하지 못해 안전하게 중지했습니다.';save();render();}},19000);
}

function addAccount(name,hiveId){
  name=name.trim(); hiveId=hiveId.trim();
  if(!name||!hiveId)throw new Error('닉네임과 Hive ID를 모두 입력해 주세요.');
  const a={id:uid(),name,hiveId,server:'korea'};
  db.accounts.push(a); db.selected.push(a.id); save();
}
function deleteAccount(id){
  db.accounts=db.accounts.filter(a=>a.id!==id);
  db.selected=db.selected.filter(x=>x!==id);
  delete db.history[id]; save();
}
function updateAccount(id,name,hiveId){
  const a=db.accounts.find(x=>x.id===id); if(!a)return;
  a.name=name.trim();a.hiveId=hiveId.trim();save();
}

function getPanel(){
  if(!document.body)return null;
  let p=document.getElementById('sw-v4');
  if(!p){
    p=document.createElement('div');p.id='sw-v4';
    Object.assign(p.style,{position:'fixed',zIndex:'2147483647',width:'410px',maxHeight:'82vh',overflow:'hidden',background:'#111',color:'#fff',border:'1px solid #444',borderRadius:'12px',boxShadow:'0 6px 24px rgba(0,0,0,.5)',font:'13px/1.45 Arial,sans-serif'});
    if(db.ui.x!=null&&db.ui.y!=null){p.style.left=db.ui.x+'px';p.style.top=db.ui.y+'px';}else{p.style.right='12px';p.style.top='12px';}
    document.body.appendChild(p);
    draggable(p);
    protectGuiTyping(p);
  } return p;
}

function protectGuiTyping(panel){
  // GUI 입력칸에서 타이핑할 때 네이버 등 원래 페이지의 검색창/단축키가 키 입력을 가로채지 못하게 함
  const inside = e => e.target && e.target.closest && e.target.closest('#sw-v4');

  ['keydown','keypress','keyup','beforeinput','input',
   'compositionstart','compositionupdate','compositionend']
    .forEach(type => {
      panel.addEventListener(type, e => {
        if (inside(e)) e.stopPropagation();
      }, false);
    });

  panel.addEventListener('mousedown', e => {
    if (inside(e) && e.target.matches('input,textarea,select,button,label')) {
      e.stopPropagation();
    }
  }, false);

  panel.addEventListener('click', e => {
    if (inside(e) && e.target.matches('input,textarea,select,button,label')) {
      e.stopPropagation();
    }
  }, false);
}
function draggable(p){
  let drag=false,ox=0,oy=0;
  p.addEventListener('mousedown',e=>{if(!e.target.closest('#sw-head')||e.target.closest('button,input,label'))return;drag=true;const r=p.getBoundingClientRect();ox=e.clientX-r.left;oy=e.clientY-r.top;p.style.right='auto';});
  addEventListener('mousemove',e=>{if(!drag)return;const x=Math.max(0,Math.min(innerWidth-p.offsetWidth,e.clientX-ox)),y=Math.max(0,Math.min(innerHeight-40,e.clientY-oy));p.style.left=x+'px';p.style.top=y+'px';db.ui.x=x;db.ui.y=y;save();});
  addEventListener('mouseup',()=>drag=false);
}

function render(){
  if(isWorker || !document.body)return; reloadDB(); const p=getPanel(); if(!p)return;
  const badge=db.newCodes.length?`<span style="background:#ffb300;color:#111;padding:2px 8px;border-radius:99px;font-weight:700">NEW ${db.newCodes.length}</span>`:`<span style="background:#333;color:#aaa;padding:2px 8px;border-radius:99px">NEW 0</span>`;
  const accounts=db.accounts.map(a=>`
    <div style="display:grid;grid-template-columns:22px 1fr 1.15fr 30px;gap:5px;align-items:center;margin:5px 0">
      <input class="ac-check" data-id="${a.id}" type="checkbox" ${db.selected.includes(a.id)?'checked':''}>
      <input class="ac-name" data-id="${a.id}" value="${esc(a.name)}" placeholder="닉네임" style="min-width:0;padding:5px;background:#222;color:#fff;border:1px solid #444;border-radius:5px">
      <input class="ac-hive" data-id="${a.id}" value="${esc(a.hiveId)}" placeholder="Hive ID" style="min-width:0;padding:5px;background:#222;color:#fff;border:1px solid #444;border-radius:5px">
      <button class="ac-del" data-id="${a.id}" title="삭제" style="height:27px;background:#522;color:#fff;border:0;border-radius:5px">×</button>
    </div>`).join('');
  const results=db.sessionResults.slice(-14).reverse().map(r=>`<div style="border-top:1px solid #333;padding:5px 0;display:flex;justify-content:space-between;gap:8px"><span><b>${esc(r.account)}</b> · ${esc(r.code)}</span><span style="color:#ffd66b;white-space:nowrap">${stat(r.status)}</span></div>`).join('');
  p.innerHTML=`
    <div id="sw-head" style="padding:10px 12px;background:#1b1b1b;border-bottom:1px solid #333;cursor:move;display:flex;justify-content:space-between;align-items:center">
      <b style="font-size:15px">SW 쿠폰 매니저 v4.7</b><div style="display:flex;gap:6px">${badge}<button id="collapse">${db.ui.collapsed?'▼':'▲'}</button></div>
    </div>
    <div style="display:${db.ui.collapsed?'none':'block'};padding:10px 12px;overflow:auto;max-height:calc(82vh - 48px)">
      ${db.newCodes.length?`<div style="background:#4a3900;border:1px solid #8a6d00;padding:9px;border-radius:8px;margin-bottom:9px"><b>새 쿠폰 발견!</b> ${db.newCodes.length}개<br><span style="color:#ffd66b">${db.newCodes.map(esc).join(' · ')}</span></div>`:''}
      <div style="color:#aaa;margin-bottom:4px">마지막 스캔: ${esc(db.lastScanAt||'아직 없음')}</div>
      <div style="color:#777;font-size:11px;margin-bottom:8px">스크립트 ON 상태에서는 일반 웹페이지에서도 GUI가 뜹니다. 시작을 누르면 쿠폰 교환소로 자동 이동하며, 공식 페이지의 실제 EVTid/쿠폰 사용 버튼 구조에 맞춰 자동 입력합니다.</div>

      <div style="border:1px solid #333;border-radius:8px;padding:8px;margin-bottom:9px">
        <div style="display:flex;justify-content:space-between;align-items:center"><b>계정 관리</b><span style="font-size:11px;color:#888">한국 서버</span></div>
        ${accounts||'<div style="color:#888;margin:7px 0">등록된 계정 없음</div>'}
        <div style="display:grid;grid-template-columns:1fr 1.15fr auto;gap:5px;margin-top:8px">
          <input id="new-name" placeholder="닉네임" style="min-width:0;padding:6px;background:#222;color:#fff;border:1px solid #444;border-radius:5px">
          <input id="new-hive" placeholder="Hive ID" style="min-width:0;padding:6px;background:#222;color:#fff;border:1px solid #444;border-radius:5px">
          <button id="add-account" style="padding:6px 9px;background:#345;color:#fff;border:0;border-radius:5px">추가</button>
        </div>
        <div style="font-size:11px;color:#777;margin-top:5px">수정한 닉네임/Hive ID는 입력칸에서 포커스를 빼면 자동 저장됩니다.</div>
      </div>

      <div style="border:1px solid #333;border-radius:8px;padding:8px;margin-bottom:9px">
        <b>실행 범위</b><br>
        <label><input type="radio" name="mode" value="new" ${db.mode==='new'?'checked':''}> 새 쿠폰만</label>
        &nbsp; <label><input type="radio" name="mode" value="all" ${db.mode==='all'?'checked':''}> 모든 활성 쿠폰</label>
        <div style="font-size:11px;color:#777;margin-top:4px">성공/이미 사용/만료/무효 기록은 다시 실행하지 않습니다.</div>
      </div>

      <div style="display:flex;gap:6px;flex-wrap:wrap;margin-bottom:9px">
        <button id="scan" style="padding:7px 10px">스캔</button>
        <button id="start" style="padding:7px 12px;background:#187d42;color:white;border:0;border-radius:6px;font-weight:700">시작</button>
        <button id="stop" style="padding:7px 12px;background:#8a2f2f;color:white;border:0;border-radius:6px;font-weight:700">정지</button>
        <button id="clear" style="padding:7px 10px">결과 지우기</button>
      </div>

      <div style="background:#181818;padding:8px;border-radius:8px;margin-bottom:9px">
        상태: <b style="color:${db.running?'#65d98a':'#bbb'}">${db.running?'실행 중':'대기'}</b> · 활성 ${db.scanCodes.length} · 새 쿠폰 ${db.newCodes.length}<br>
        진행: ${db.queue.length?Math.min(db.queueIndex+(db.running?1:0),db.queue.length):0} / ${db.queue.length}<br>
        <span style="color:#ccc">${esc(db.lastMessage)}</span>
        ${db.lastError?`<div style="color:#ff8c8c">${esc(db.lastError)}</div>`:''}
      </div>

      <details ${db.newCodes.length?'open':''}><summary><b>새 쿠폰 (${db.newCodes.length})</b></summary><div style="padding:6px;color:#ffd66b">${db.newCodes.map(c=>`<div>${esc(c)}</div>`).join('')||'<span style="color:#777">없음</span>'}</div></details>
      <details><summary><b>활성 쿠폰 전체 (${db.scanCodes.length})</b></summary><div style="padding:6px;color:#aaa">${db.scanCodes.map(c=>`<div>${esc(c)}</div>`).join('')}</div></details>
      <div style="margin-top:8px"><b>이번 실행 결과</b><span style="color:#777;font-size:11px;margin-left:6px">상세 서버 메시지 숨김</span>${results||'<div style="color:#777;margin-top:4px">아직 결과 없음</div>'}</div>
    </div>`;

  p.querySelector('#collapse').onclick=()=>{db.ui.collapsed=!db.ui.collapsed;save();render();};
  p.querySelectorAll('.ac-check').forEach(x=>x.onchange=()=>{x.checked?(!db.selected.includes(x.dataset.id)&&db.selected.push(x.dataset.id)):(db.selected=db.selected.filter(i=>i!==x.dataset.id));save();});
  p.querySelectorAll('.ac-name').forEach(x=>x.onchange=()=>{const a=db.accounts.find(a=>a.id===x.dataset.id);if(a){a.name=x.value.trim();save();}});
  p.querySelectorAll('.ac-hive').forEach(x=>x.onchange=()=>{const a=db.accounts.find(a=>a.id===x.dataset.id);if(a){a.hiveId=x.value.trim();save();}});
  p.querySelectorAll('.ac-del').forEach(x=>x.onclick=()=>{if(confirm('이 계정을 목록에서 삭제할까요?')){deleteAccount(x.dataset.id);render();}});
  p.querySelector('#add-account').onclick=()=>{try{addAccount(p.querySelector('#new-name').value,p.querySelector('#new-hive').value);render();}catch(e){db.lastError=e.message;save();render();}};
  p.querySelectorAll('input[name="mode"]').forEach(x=>x.onchange=()=>{db.mode=x.value;save();});
  p.querySelector('#scan').onclick=()=>doScan().catch(()=>{});
  p.querySelector('#start').onclick=()=>startRun();
  p.querySelector('#stop').onclick=()=>{reloadDB();db.running=false;db.waiting=false;db.current=null;db.lastMessage='사용자가 정지했습니다.';save();render();};
  p.querySelector('#clear').onclick=()=>{db.sessionResults=[];save();render();};
}

let valueListenerInstalled=false;

function installLiveGuiSync(){
  if(isWorker || valueListenerInstalled) return;
  valueListenerInstalled=true;
  try{
    GM_addValueChangeListener(KEY,()=>{
      setTimeout(()=>render(),30);
    });
  }catch{}
}

async function boot(){
  if(isWorker){
    // 작업 전용 탭: GUI 없이 저장된 큐만 처리.
    reloadDB();
    if(db.running&&!db.waiting){
      setTimeout(runOne,900);
    }
    return;
  }

  installLiveGuiSync();
  render();

  // 일반 탭에서는 최근 5분 내 스캔이 없을 때만 자동 스캔.
  reloadDB();
  const last=Date.parse(db.lastScanAt||'')||0;
  if(!last || Date.now()-last>5*60*1000){
    try{await doScan();}catch{}
  }else{
    db.lastMessage=db.newCodes?.length
      ? `새 쿠폰 ${db.newCodes.length}개 발견!`
      : `최근 스캔 기준 새 쿠폰 없음 · 활성 ${db.scanCodes?.length||0}개`;
    save();render();
  }
}

addEventListener('DOMContentLoaded',()=>setTimeout(boot,650));
addEventListener('load',()=>setTimeout(()=>{
  if(isWorker){
    reloadDB();
    if(db.running&&!db.waiting)runOne();
  }else{
    installLiveGuiSync();
    render();
  }
},1700));
})();
