(() => {
  'use strict';
  if (location.hostname !== 'map.baidu.com' || window.__baiduDesktop) return;
  const adSelectors = [
    '#message-panel', '#app-download', '#ad-container',
    '.ad-banner', '.advertisement', '.download-guide', '.download-app-guide',
    '.download-app-popup', '.promotional-banner',
    // Official ui3MapSuggest / DynamicBanner: both the old QR coupon card
    // and its newer implementation, including route download promotions.
    '#leadDownloadCard', '.poiLeadDownloadCard', '.leadDownloadCard', '.trafficLeadDownloadCard', '.bannerNew-suggest',
    '.download-banner', '.banner-new', '.banner-new-traffic'
  ];
  const promotionSelector = adSelectors.join(',');
  const detailSelector = '.poidetail-container,#generalheader';
  const quietControlSelector = '#map-operate,#mapType-wrapper,#newuilogo,.BMap_scaleCtrl,#scaleCtrl';
  const irrelevantLinks = new Set(['百度营销', '商业合作', '品牌/商户认领', '下载地图客户端']);
  const send = data => { try { window.chrome.webview.postMessage(JSON.stringify(data)); } catch {} };
  const clean = () => {
    if (!document.head) return;
    if (!document.getElementById('baidu-desktop-clean')) {
      const style = document.createElement('style');
      style.id = 'baidu-desktop-clean';
      style.textContent = promotionSelector + '{display:none!important;visibility:hidden!important;pointer-events:none!important}' +
        '@font-face{font-family:"BMD Sans SC";src:url("https://map.baidu.com/__desktop-assets/modern-sans.woff2") format("woff2");font-weight:400;font-style:normal;font-display:swap}' +
        // Restrict typography to the left search/detail/route UI. Map canvas,
        // labels, markers and icon-font elements keep their original fonts.
        ':is(#sole-searchbox-content,#route-searchbox-content,#cards-level1,#cards-level2,#ui3-suggest-wrap){font-family:"BMD Sans SC","Microsoft YaHei",sans-serif!important}' +
        ':is(#sole-searchbox-content,#route-searchbox-content,#cards-level1,#cards-level2,#ui3-suggest-wrap) :is(input,textarea,button,p,a,h1,h2,h3,h4,li,td,th,label,div,span):not([class*="icon"]):not([class*="Icon"]):not([class*="ICON"]){font-family:"BMD Sans SC","Microsoft YaHei",sans-serif!important}' +
        '#sole-input,#route-searchbox-content input{font-size:14px!important;letter-spacing:.15px}' +
        '#cards-level1 :is(h1,h2,h3),#cards-level2 :is(h1,h2,h3){letter-spacing:.2px}' +
        '#MapHolder .BMap_cpyCtrl,#copy-right{opacity:.03!important;background:transparent!important;transition:opacity .15s ease}' +
        '#MapHolder .BMap_cpyCtrl *,#copy-right *{background:transparent!important}' +
        '.BMap_cpyCtrl a,#copy-right a{display:none!important;visibility:hidden!important;pointer-events:none!important}' +
        '#render-mode-tips,#renderModeSwitch .black-tips{display:none!important;visibility:hidden!important;pointer-events:none!important}' +
        ':is(' + quietControlSelector + '){opacity:.2!important;transition:opacity .2s ease-out!important}' +
        '#map-operate{transition:opacity .2s ease-out,transform .3s ease-out!important}' +
        ':is(' + quietControlSelector + '):is(:hover,:focus-within,:active,.bmd-control-near){opacity:1!important}' +
        '.bmd-closable-detail{position:relative!important;padding-right:32px!important;box-sizing:border-box!important}' +
        '.bmd-closable-detail .generalHead-titlename{max-width:100%!important}' +
        '.bmd-detail-close{position:absolute!important;right:4px!important;top:6px!important;width:26px!important;height:26px!important;padding:5px!important;box-sizing:border-box!important;border:0!important;border-radius:6px!important;background:transparent!important;color:#fff!important;cursor:pointer!important;z-index:10!important;display:flex!important;align-items:center;justify-content:center}' +
        '.bmd-detail-close:hover{background:rgba(255,255,255,.2)!important}.bmd-detail-close:focus-visible{outline:2px solid white!important;outline-offset:1px}' +
        '.bmd-detail-close svg{width:16px;height:16px;pointer-events:none}' +
        // Retain the site's internal controller anchors, but remove the entire
        // account/tool strip from layout instead of covering it with our header.
        '#app-right-top,#user-center,#message-center{display:none!important;visibility:hidden!important;pointer-events:none!important}' +
        '#tool-container{position:fixed!important;left:-10000px!important;top:-10000px!important;width:425px!important;visibility:hidden!important;pointer-events:none!important}' +
        // The real WPF caption occupies its own row. The webview begins below
        // it; no HTML header surface or independent header HWND is needed.
        'body.bmd-native-header{--bmd-search-width:clamp(164px,calc(100vw - 620px),425px)}' +
        'body.bmd-native-header:not(.bmd-fullscreen) #left-panel{left:12px!important;top:10px!important;overflow:visible!important}' +
        'body.bmd-native-header:not(.bmd-fullscreen) #sole-searchbox-content{position:absolute!important;left:-10000px!important;top:0!important;visibility:hidden!important;pointer-events:none!important}' +
        'body.bmd-native-header:not(.bmd-fullscreen) #search-button,body.bmd-native-header:not(.bmd-fullscreen) #tooltip-route{display:none!important}' +
        'body.bmd-native-header:not(.bmd-fullscreen) #searchbox{width:368px!important}' +
        'body.bmd-native-header:not(.bmd-fullscreen) #searchbox-container{width:100%!important;float:none!important;box-shadow:none!important}' +
        'body.bmd-native-header:not(.bmd-fullscreen) .route-searchbox-content{width:100%!important}' +
        'body.bmd-native-suggestions:not(.bmd-fullscreen) #ui3-suggest-wrap{position:fixed!important;top:0!important;left:48px!important;width:var(--bmd-search-width)!important;max-height:calc(100vh - 12px)!important;overflow-y:auto!important;z-index:10010!important}' +
        'body.bmd-native-header:not(.bmd-fullscreen) #cards-level0,body.bmd-native-header:not(.bmd-fullscreen) #cards-level1,body.bmd-native-header:not(.bmd-fullscreen) #cards-level2{max-height:calc(100vh - 24px)}' +
        '.bmd-native-tool-popup{position:fixed!important;right:14px!important;left:auto!important;top:12px!important;z-index:10010!important;visibility:visible!important;max-height:calc(100vh - 28px)!important;overflow:auto!important}' +
        '.bmd-native-share{width:330px!important}.bmd-native-traffic{width:317px!important}' +
        '#MapHolder .BMap_cpyCtrl:hover,#MapHolder .BMap_cpyCtrl:focus-within,#copy-right:hover,#copy-right:focus-within{opacity:.9!important}';
      document.head.appendChild(style);
    }
    installNativeHeader();
    installDetailCloseButtons();
    // Keep map layers, login dialogs, place/route content and the required
    // copyright/registration text. Footer links are hidden separately by CSS.
    for (const selector of adSelectors) {
      for (const node of document.querySelectorAll(selector)) node.remove();
    }
    for (const link of document.querySelectorAll('#MapHolder a, #copy-right a, .BMap_cpyCtrl a')) {
      if (irrelevantLinks.has(link.textContent.trim())) link.style.display = 'none';
    }
  };
  let cleanTimer;
  const observer = new MutationObserver(records => {
    // Map dragging changes tiles continuously. Only schedule DOM work when
    // a known promotion is actually inserted; CSS already hides it immediately.
    const promotionAdded = records.some(record => Array.from(record.addedNodes).some(node =>
      node.nodeType === 1 && (node.matches(promotionSelector) || (node.firstElementChild && node.querySelector(promotionSelector)))));
    const detailAdded = records.some(record => Array.from(record.addedNodes).some(node =>
      node.nodeType === 1 && (node.matches(detailSelector) || (node.firstElementChild && node.querySelector(detailSelector)))));
    if (!promotionAdded && !detailAdded) return;
    // Throttle rather than debounce: continuous map updates cannot postpone
    // removing a newly inserted banner indefinitely. CSS prevents ad flashes.
    if (!cleanTimer) cleanTimer = setTimeout(() => { cleanTimer = undefined; clean(); }, 80);
  });
  observer.observe(document, { childList: true, subtree: true });
  document.addEventListener('DOMContentLoaded', clean, { once: true });
  if (document.readyState !== 'loading') clean();

  // Keep the controls in their original layout and preserve every click/drag
  // handler. Only their composited opacity changes; proximity is four CSS px.
  let controlPointer=null, controlFrame=0;
  function updateQuietControls() {
    controlFrame=0;
    const states=Array.from(document.querySelectorAll(quietControlSelector)).map(node=>{
      let near=false;
      if(controlPointer) {
        const own=node.getBoundingClientRect();
        // Baidu positions several compass/zoom children absolutely inside a
        // zero-size wrapper, so use their real boxes rather than a broad zone.
        const elements=own.width>0 && own.height>0 ? [node] : Array.from(node.querySelectorAll('*'));
        near=elements.some(element=>{
          const box=element.getBoundingClientRect();
          return box.width>0 && box.height>0 && getComputedStyle(element).visibility!=='hidden' &&
            controlPointer.x>=box.left-4 && controlPointer.x<=box.right+4 &&
            controlPointer.y>=box.top-4 && controlPointer.y<=box.bottom+4;
        });
      }
      return {node,near};
    });
    // Batch geometry reads before style writes to avoid repeated layouts while
    // moving over or dragging the map.
    for(const {node,near} of states) node.classList.toggle('bmd-control-near',near);
  }
  function scheduleQuietControls() {
    if(!controlFrame) controlFrame=requestAnimationFrame(updateQuietControls);
  }
  document.addEventListener('pointermove',event=>{
    if(event.pointerType==='touch') return;
    controlPointer={x:event.clientX,y:event.clientY};
    scheduleQuietControls();
  },{capture:true,passive:true});
  document.addEventListener('pointerleave',()=>{controlPointer=null;scheduleQuietControls();});
  window.addEventListener('blur',()=>{controlPointer=null;scheduleQuietControls();});
  window.addEventListener('resize',scheduleQuietControls,{passive:true});

  let lastQuery = '';
  let lastQueryAt = 0;
  const recordSearch = () => {
    const input = document.getElementById('sole-input');
    const query = input ? input.value.trim() : '';
    if (!query || (query === lastQuery && Date.now() - lastQueryAt < 1200)) return;
    lastQuery = query;
    lastQueryAt = Date.now();
    send({ type: 'search', query });
  };
  document.addEventListener('click', event => {
    if (event.target instanceof Element && event.target.closest('#search-button')) recordSearch();
    if (event.target instanceof Element && event.target.closest('#ui3-suggest-wrap .ui3-suggest-item')) setTimeout(recordSearch,0);
    if (event.target instanceof Element && !event.target.closest('#ui3-suggest-wrap,#sole-input')) hideNativeSuggestions();
  }, true);
  document.addEventListener('keydown', event => {
    if (event.key === 'Enter' && event.target && event.target.id === 'sole-input') recordSearch();
  }, true);

  const routeButton = () => document.querySelector('#sole-searchbox-content .route-button, #sole-searchbox-content [title="路线"], #sole-searchbox-content [data-title="路线"]');
  function closePlaceDetails() {
    const original=document.querySelector('#sole-searchbox-content .cancel-button');
    if(original) {
      original.click();
      send({type:'search-display',query:''});
      return;
    }
    const loader=typeof require==='function'?require:window.require;
    if(!loader || typeof loader.async!=='function') return;
    loader.async(['common:widget/ui/card/cardMgr.js','common:widget/ui/searchBox/searchBox.js'],(cards,search)=>{
      // Match the native cancel action, including its internal card state,
      // markers and search state. Never just hide the DOM of a live card.
      if(window.listener) window.listener.trigger('app','reset');
      cards.clear();
      search.setState('sole',null,{reset:true,resetInput:true});
      send({type:'search-display',query:''});
    });
  }
  function installDetailCloseButtons() {
    for(const detail of document.querySelectorAll('.poidetail-container')) {
      const header=detail.querySelector('#generalheader,.generalHead-header');
      if(!header || header.querySelector('.bmd-detail-close')) continue;
      header.classList.add('bmd-closable-detail');
      const button=document.createElement('button');
      button.type='button';button.className='bmd-detail-close';
      button.title='关闭地点详情';button.setAttribute('aria-label','关闭地点详情');
      button.innerHTML='<svg viewBox="0 0 20 20" aria-hidden="true"><path d="M4 4L16 16M16 4L4 16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round"/></svg>';
      button.addEventListener('click',event=>{event.preventDefault();event.stopPropagation();closePlaceDetails();});
      header.appendChild(button);
    }
  }
  function installNativeHeader() {
    if (!document.body || window.top !== window) return;
    if (!document.body.classList.contains('bmd-native-header')) {
      document.body.classList.add('bmd-native-header');
      send({type:'native-header',query:'ready'});
    }
  }
  function hideNativeSuggestions() {
    // Leave route-input suggestions alone; their official UI remains in the
    // map content below the caption.
    if (!document.body?.classList.contains('bmd-native-suggestions')) return;
    document.body.classList.remove('bmd-native-suggestions');
    if (window.ui3MapSuggestWrap) window.ui3MapSuggestWrap.hide();
  }
  function setSearchInput(query) {
    const input=document.getElementById('sole-input');
    if (!input) return null;
    const setter=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;
    setter.call(input,query);
    input.dispatchEvent(new Event('input',{bubbles:true}));
    input.dispatchEvent(new Event('change',{bubbles:true}));
    return input;
  }
  function searchKey(input,code) {
    input.dispatchEvent(new KeyboardEvent('keydown',{bubbles:true,keyCode:code,which:code}));
    input.dispatchEvent(new KeyboardEvent('keyup',{bubbles:true,keyCode:code,which:code}));
  }
  function withSoleSearch(action) {
    const loader=typeof require==='function'?require:window.require;
    if (!loader || typeof loader.async!=='function' || !document.getElementById('sole-input')) return false;
    loader.async('common:widget/ui/searchBox/searchBox.js',box=>{
      if (box.getState().state==='sole') action(box);
      else box.setState('sole').then(()=>action(box));
    });
    return true;
  }
  let nativeToolsHooked = false, popupTimer, popupDeadline;
  const reportTraffic = () => {
    const active = !!window.trafficCtrl?.opened;
    send({type:'traffic-state',query:active ? 'on' : 'off'});
  };
  const settleTools = () => { reportTraffic(); setTimeout(reportTraffic, 200); setTimeout(reportTraffic, 900); };
  const connectNativeTools = () => {
    if (!nativeToolsHooked && window.listener && typeof window.listener.on === 'function') {
      nativeToolsHooked = true;
      window.listener.on('index.controls', 'close', settleTools);
    }
  };
  document.addEventListener('DOMContentLoaded', () => { connectNativeTools(); reportTraffic(); }, {once:true});
  document.addEventListener('click', event => {
    if (event.target instanceof Element && event.target.closest('#ui3_control_wrap, #traffic_container, #traffic_control')) settleTools();
  }, true);
  const relocateToolPopups = action => {
    let located = false;
    for (const [popup, kind] of [[window.trafficCtrl?.pop,'traffic'],[window.temp?.linkPop,'share']]) {
      const node = popup && typeof popup.getDom === 'function' ? popup.getDom() : null;
      if (!node || !node.isConnected) continue;
      if (node.parentNode !== document.body) document.body.appendChild(node);
      node.classList.add('bmd-native-tool-popup','bmd-native-'+kind);
      if (kind === action) located = true;
    }
    return located;
  };
  const followToolPopup = action => {
    clearInterval(popupTimer);clearTimeout(popupDeadline);
    const follow = () => {reportTraffic();if(relocateToolPopups(action)){clearInterval(popupTimer);clearTimeout(popupDeadline);}};
    popupTimer=setInterval(follow,80);popupDeadline=setTimeout(()=>clearInterval(popupTimer),10000);follow();
  };
  const mapTool = action => {
    if (!['traffic','measure','share'].includes(action)) return false;
    connectNativeTools();
    const loader = typeof require === 'function' ? require : window.require;
    if (!loader || typeof loader.async !== 'function') return false;
    const unavailable = () => send({type:'map-tool-error',query:action});
    if (action === 'measure') loader.async('common:widget/ui/ui3Tools/ui3Tools.js', tools => {
      try { if (typeof tools.doMeasure !== 'function') return unavailable(); tools.doMeasure(); }
      catch { unavailable(); }
    });
    else if (action === 'share') loader.async('common:widget/ui/toolShare/ToolShare.js', tools => {
      try {
        if (window.temp?.detailInfoPop) window.temp.detailInfoPop.close();
        if (typeof tools.open !== 'function') return unavailable();
        window.toolbarwidth=332;
        tools.open({from:'mapshare',keepTraffic:!!window.trafficCtrl?.opened});
        followToolPopup('share');
      } catch { unavailable(); }
    });
    else loader.async('common:widget/ui/Traffic/Traffic.js', tools => {
      try {
        if (!tools.need2ShowTraffic()) return unavailable();
        if (!document.getElementById('traffic_control')) {
          const state=document.createElement('span');state.id='traffic_control';state.hidden=true;document.body.appendChild(state);
        }
        if (window.trafficCtrl) window.trafficCtrl.opened ? window.trafficCtrl.close() : window.trafficCtrl.open();
        else tools.loadTrafficCtrl();
        settleTools();followToolPopup('traffic');
      } catch { unavailable(); }
    });
    return true;
  };
  window.__baiduDesktop = {
    mapTool,
    fullScreen(enabled) { hideNativeSuggestions();document.body?.classList.toggle('bmd-fullscreen',!!enabled); },
    hideNativeSuggestions,
    nativeSuggest(query) {
      return withSoleSearch(()=>{
        const input=setSearchInput(query);
        if (!input) return;
        document.body.classList.add('bmd-native-suggestions');
        // Official autocomplete listens to keydown/keyup rather than input.
        // Synthetic events preserve focus in the native textbox, including IME.
        searchKey(input,65);
        if (!query) input.click();
      });
    },
    nativeSearchKey(code) {
      const input=document.getElementById('sole-input');
      if (!input || !document.body.classList.contains('bmd-native-suggestions')) return null;
      searchKey(input,code);
      return input.value;
    },
    nativeSubmit(query) {
      const selected=document.body.classList.contains('bmd-native-suggestions') && document.querySelector('#ui3-suggest-wrap .ui3-suggest-item-hover');
      if (selected) { selected.click();hideNativeSuggestions();return true; }
      return this.search(query);
    },
    route() {
      hideNativeSuggestions();
      const loader=typeof require==='function'?require:window.require;
      if (loader && typeof loader.async==='function') {
        loader.async('common:widget/ui/searchBox/searchBox.js',box=>box.setState('route'));
        return true;
      }
      const button = routeButton();
      if (button) { button.click(); return true; }
      return false;
    },
    search(query) {
      hideNativeSuggestions();
      return withSoleSearch(box=>{
        const input=setSearchInput(query);
        if (!input) return;
        searchKey(input,27);
        box.search({from:'user'});
        recordSearch();
      });
    },
    focusSearch() { const input = document.getElementById('sole-input'); if (input) { input.focus(); input.select(); } },
    label() {
      const input = document.getElementById('sole-input');
      return input && input.value.trim() ? input.value.trim() : '当前地图';
    },
    clean
  };
})();
