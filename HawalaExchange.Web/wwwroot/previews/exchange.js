/* Isolated UI prototype: no fetch, database, storage, authentication or financial posting. */
(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const number = value => Number(value).toLocaleString('en-US');
  const icon = name => `<svg aria-hidden="true"><use href="#${name}"/></svg>`;
  const transfers = [
    [2172,'A44559','عبدالله نظری','عبدالجمیل','قندوز',21560,'AFN','پرداخت شده','کویته','دریافتی','2026-10-18','1405/07/26'],
    [2173,'A44560','احمد ولی','محمد ناصر','کابل برچی',1000,'USD','ارسال شده','کابل برچی','ارسالی','2026-10-18','1405/07/26'],
    [2174,'A44561','علی رضایی','حسین احمدی','هرات',85000,'AFN','در انتظار اجرا','هرات','دریافتی','2026-10-18','1405/07/26'],
    [2175,'A44562','محمد اکبری','فاطمه اکبری','خود صرافی',2500,'USD','در انتظار اجرا','کویته','دریافتی','2026-10-18','1405/07/26'],
    [2176,'A44563','نصیر احمد','حبیب‌الله','کابل برچی',150000,'AFN','ارسال شده','کابل برچی','ارسالی','2026-10-17','1405/07/25'],
    [2177,'A44564','رضا محمدی','احمد جاوید','قندوز',42000,'AFN','پرداخت شده','کویته','دریافتی','2026-10-17','1405/07/25'],
    [2178,'A44565','عبدالرحمان','مهدی رحیمی','هرات',800,'USD','پرداخت شده','هرات','دریافتی','2026-10-17','1405/07/25'],
    [2179,'A44566','سید مصطفی','نادر علی','خود صرافی',65000,'AFN','در انتظار اجرا','کویته','دریافتی','2026-10-17','1405/07/25']
  ].map(r => ({id:r[0],ref:r[1],sender:r[2],receiver:r[3],place:r[4],amount:r[5],currency:r[6],status:r[7],agent:r[8],kind:r[9],date:r[10],displayDate:r[11]}));
  const views = {
    hawalas:{title:'حواله‌ها',subtitle:'ثبت، پیگیری و مدیریت حواله‌ها در یک فضای کاری',create:'ثبت حواله',cols:['نمبر مسلسل / حواله','فرستنده / گیرنده','محل پرداخت','مبلغ','نوع / وضعیت','تاریخ']},
    home:{title:'نمای کلی',subtitle:'نمونه‌ای از خلاصه و فعالیت‌های روزانه صرافی',create:'ثبت حواله',cols:['نمبر مسلسل / حواله','فرستنده / گیرنده','محل پرداخت','مبلغ','نوع / وضعیت','تاریخ']},
    customers:{title:'مشتریان',subtitle:'جستجوی یک‌جا در مشخصات و دسترسی سریع به فعالیت مشتری',create:'افزودن مشتری',cols:['کد مشتری','نام کامل / نام پدر','شماره تماس','وضعیت','آخرین فعالیت'],rows:[['C001','احمد ولی / محمد ولی','0700000001','فعال','1405/07/26'],['C002','علی رضایی / محمد رضا','0700000002','فعال','1405/07/26'],['C003','محمد اکبری / عبدالله','0700000003','فعال','1405/07/25'],['C004','رضا محمدی / ناصر','0700000004','غیرفعال','1405/07/25']]},
    correspondents:{title:'نمایندگی‌ها',subtitle:'حساب و کمیشن نمایندگی‌ها، با تفکیک مانده و کمیشن باز',create:'افزودن نمایندگی',cols:['کد / نام نمایندگی','ارز','مانده بدون کمیشن','کمیشن باز','مجموع با کمیشن','وضعیت'],rows:[['AG001 · کویته','USD','12,000 بدهکار','400 بدهکار','12,400 بدهکار','فعال'],['AG002 · کابل برچی','AFN','150,000 طلبکار','600 طلبکار','150,600 طلبکار','فعال'],['AG003 · هرات','USD','3,000 طلبکار','12 طلبکار','3,012 طلبکار','فعال']]},
    accounts:{title:'حساب‌ها',subtitle:'کد حساب و صاحب حساب در یک جستجوی مشترک',create:'افزودن حساب',cols:['کد حساب','نام حساب / صاحب حساب','نوع','ارز','مانده','وضعیت'],rows:[['AC001','صندوق مرکزی / صرافی','صندوق','AFN','850,000 طلبکار','فعال'],['AC002','صندوق دالر / صرافی','صندوق','USD','25,000 طلبکار','فعال'],['AC003','حساب بانکی / صرافی','بانک','AFN','1,200,000 طلبکار','فعال']]},
    journal:{title:'روزنامچه',subtitle:'مرور عملیات با شرح روشن و تفکیک رسید و برد',create:'ثبت سند نمونه',cols:['نمبر سند','نوع عملیات','شرح','مبلغ','تاریخ'],rows:[['J001','رسید','رسید از احمد ولی به صندوق مرکزی','25,000 AFN','1405/07/26'],['J002','برد','برد از صندوق دالر برای علی رضایی','500 USD','1405/07/26'],['J003','حواله','حواله دریافتی از عبدالله نظری به عبدالجمیل · قندوز','21,560 AFN','1405/07/26']]},
    commissions:{title:'کمیشن‌ها',subtitle:'کمیشن‌ها به تفکیک نوع، نمایندگی و روز محاسبه',create:'پیش‌نمایش محاسبه',cols:['نوع کمیشن','نمایندگی','تاریخ','مبنای محاسبه','کمیشن','وضعیت'],rows:[['دریافتی','کویته','1405/07/26','100,000 USD','400 USD','محاسبه‌شده'],['پرداختی به نمایندگی','کابل برچی','1405/07/26','500,000 AFN','2,000 AFN','محاسبه‌شده'],['ارسالی به نمایندگی‌ها','کویته','1405/07/25','50,000 USD','200 USD','محاسبه‌شده']]},
    reports:{title:'گزارش‌ها',subtitle:'الگوی مشترک پیش‌نمایش، فیلتر زمانی و خروجی گزارش',create:'پیش‌نمایش گزارش',cols:['نام گزارش','بازه نمونه','تفکیک','وضعیت'],rows:[['گزارش فعالیت مشتری','1405/07/01 تا 1405/07/26','ارز و مانده اول دوره','آماده پیش‌نمایش'],['گزارش فعالیت نمایندگی','1405/07/01 تا 1405/07/26','نمایندگی و ارز','آماده پیش‌نمایش'],['گزارش حواله و کمیشن','1405/07/01 تا 1405/07/26','نوع کمیشن و روز','آماده پیش‌نمایش']]}
  };
  let current = 'hawalas', page = 1, selected = new Set(), noticeTimer, dialogMode;
  const isTransfer = () => ['hawalas','home'].includes(current);
  const badge = text => `<span class="badge ${['پرداخت شده','فعال','رسید'].includes(text)?'green':text==='در انتظار اجرا'?'amber':'blue'}">${esc(text)}</span>`;
  function notify(text) { $('notice').textContent=text; $('notice').classList.add('visible'); clearTimeout(noticeTimer); noticeTimer=setTimeout(()=>$('notice').classList.remove('visible'),5500); }
  function rows() {
    const query=$('search').value.trim().toLowerCase();
    if (!isTransfer()) return views[current].rows.map((cells,i)=>({id:i+1,cells})).filter(r=>r.cells.join(' ').toLowerCase().includes(query));
    return transfers.filter(r=>Object.values(r).join(' ').toLowerCase().includes(query) && (!$('status').value||r.status===$('status').value) && (!$('currency').value||r.currency===$('currency').value) && (!$('from-date').value||r.date >= $('from-date').value) && (!$('to-date').value||r.date <= $('to-date').value) && (!$('correspondent').value||r.agent===$('correspondent').value) && (!$('location').value||r.place===$('location').value) && (!$('kind').value||r.kind===$('kind').value));
  }
  function render() {
    const all=rows(), size=Number($('page-size').value), pages=Math.max(1,Math.ceil(all.length/size)); page=Math.min(page,pages);
    const visible=all.slice((page-1)*size,page*size), cols=views[current].cols;
    $('table-head').innerHTML=`<tr><th><label class="check-target"><input id="select-all" type="checkbox" aria-label="انتخاب همه ردیف‌های همین صفحه"></label></th><th>ردیف</th><th>عملیات</th>${cols.map(c=>`<th scope="col">${esc(c)}</th>`).join('')}</tr>`;
    $('table-body').innerHTML=visible.length ? visible.map((r,i)=>`<tr class="${selected.has(r.id)?'selected':''}"><td><label class="check-target"><input data-select="${r.id}" type="checkbox" aria-label="انتخاب ردیف ${(page-1)*size+i+1}" ${selected.has(r.id)?'checked':''}></label></td><td>${(page-1)*size+i+1}</td><td><button class="button row-action" data-detail="${r.id}" aria-label="مشاهده جزئیات ردیف ${(page-1)*size+i+1}">${icon('more')}</button></td>${isTransfer()?`<td><button class="link" data-detail="${r.id}">${r.id}</button><small>${esc(r.ref)}</small></td><td>${esc(r.sender)}<small>به ${esc(r.receiver)}</small></td><td>${esc(r.place)}</td><td class="amount">${number(r.amount)} <small>${r.currency}</small></td><td>${badge(r.status)}<small>${esc(r.kind)}</small></td><td dir="ltr">${r.displayDate}</td>`:r.cells.map((cell,index)=>`<td>${index===0?`<button class="link" data-detail="${r.id}">${esc(cell)}</button>`:cell==='فعال'||cell==='غیرفعال'?badge(cell):esc(cell)}</td>`).join('')}</tr>`).join(''):`<tr><td colspan="${cols.length+3}" class="empty">نتیجه‌ای پیدا نشد. عبارت جستجو یا فیلترها را تغییر دهید.</td></tr>`;
    const header=$('select-all'); header.checked=visible.length>0&&visible.every(r=>selected.has(r.id)); header.indeterminate=!header.checked&&visible.some(r=>selected.has(r.id)); header.disabled=visible.length===0;
    header.onchange=()=>{visible.forEach(r=>header.checked?selected.add(r.id):selected.delete(r.id));render();};
    document.querySelectorAll('[data-select]').forEach(input=>input.onchange=()=>{input.checked?selected.add(Number(input.dataset.select)):selected.delete(Number(input.dataset.select));render();});
    document.querySelectorAll('[data-detail]').forEach(button=>button.onclick=()=>showDetails(Number(button.dataset.detail)));
    $('result-count').textContent=`${all.length} مورد`;
    $('footer-count').textContent=all.length?`نمایش ${(page-1)*size+1} تا ${Math.min(page*size,all.length)} از ${all.length} مورد`:'بدون نتیجه';
    $('page-number').textContent=`${page} / ${pages}`; $('prev').disabled=page===1; $('next').disabled=page===pages;
    $('selection-bar').hidden=!selected.size; $('selected-count').textContent=`${selected.size} مورد انتخاب شد`; $('selection-hint').hidden=!!selected.size;
    $('execute').hidden=!isTransfer();
    $('total-count').innerHTML=`${all.length} <small>حواله</small>`;
    for(const code of ['AFN','USD']) $(`${code.toLowerCase()}-total`).innerHTML=`${number(all.filter(r=>r.currency===code).reduce((n,r)=>n+r.amount,0))} <small>${code}</small>`;
    $('pending-total').innerHTML=`${all.filter(r=>r.status==='در انتظار اجرا').length} <small>حواله</small>`;
  }
  function switchPage(key) {
    current=key;page=1;selected.clear();$('filters').reset();
    for(const [id,text] of [['page-title',views[key].title],['breadcrumb',views[key].title],['list-title',`لیست ${views[key].title}`],['page-subtitle',views[key].subtitle],['create-label',views[key].create]]) $(id).textContent=text;
    document.querySelectorAll('[data-page]').forEach(b=>{b.removeAttribute('aria-current');if(b.dataset.page===key)b.setAttribute('aria-current','page');});
    document.querySelector('.summary').hidden=!isTransfer();
    for (const id of ['status','currency','from-date','to-date','more-filters']) $(id).closest('.field') ? $(id).closest('.field').hidden=!isTransfer():$(id).hidden=!isTransfer();
    $('bulk').hidden=!isTransfer();$('advanced').hidden=true;$('more-filters').setAttribute('aria-expanded','false');
    $('search').placeholder=isTransfer()?'نمبر، نام یا محل پرداخت…':'کد، نام یا سایر مشخصات…'; render();
  }
  function openDialog(title,body,mode='details') { dialogMode=mode;$('dialog-title').textContent=title;$('dialog-body').innerHTML=body;$('dialog-submit').textContent=mode==='create'?'بررسی نمونه':'بستن پیش‌نمایش';$('record-dialog').showModal(); }
  function showDetails(id) {
    const r=rows().find(x=>x.id===id); if(!r)return;
    const labels=isTransfer()?['نمبر مسلسل','نمبر حواله','فرستنده','گیرنده','محل پرداخت','مبلغ','وضعیت','تاریخ']:views[current].cols;
    const values=isTransfer()?[r.id,r.ref,r.sender,r.receiver,r.place,`${number(r.amount)} ${r.currency}`,r.status,r.displayDate]:r.cells;
    openDialog('جزئیات نمونه',`<p class="wide">اطلاعات زیر ساختگی است و فقط چیدمان جزئیات را نشان می‌دهد.</p><dl class="wide">${labels.map((l,i)=>`<dt>${esc(l)}</dt><dd>${esc(values[i])}</dd>`).join('')}</dl>`);
  }
  $('create').onclick=()=> {
    if(!isTransfer()){openDialog(views[current].create,'<p class="wide">این بخش نمونه‌ای از فرم یک‌دست است؛ اطلاعاتی در سیستم ثبت نمی‌شود.</p><label class="field wide"><span>نام / عنوان نمونه</span><input required maxlength="100" placeholder="نام را وارد کنید"></label><label class="field wide"><span>یادداشت</span><textarea placeholder="توضیحات اختیاری"></textarea></label>','create');return;}
    openDialog('ثبت حواله جدید',`<p class="wide">فرم نمایشی است؛ با بررسی نمونه هیچ حواله یا سند حسابداری ثبت نمی‌شود.</p><label class="field"><span>نوع حواله</span><select><option>دریافتی</option><option>ارسالی</option></select></label><label class="field"><span>تاریخ</span><input type="date" value="2026-10-18" required></label><label class="field"><span>فرستنده *</span><input required autocomplete="off" placeholder="نام فرستنده"></label><label class="field"><span>گیرنده *</span><input required autocomplete="off" placeholder="نام گیرنده"></label><label class="field"><span>مبلغ *</span><input type="number" min="1" step="1" required placeholder="0" dir="ltr"></label><label class="field"><span>ارز</span><select><option>AFN</option><option>USD</option><option>PKR</option></select></label><label class="field"><span>محل پرداخت</span><select><option>خود صرافی</option><option>قندوز</option><option>کابل برچی</option><option>هرات</option></select></label><label class="field"><span>نمایندگی</span><select><option>کویته</option><option>کابل برچی</option><option>هرات</option></select></label><label class="field"><span>نمبر حواله</span><input placeholder="مثلاً A44559"></label><label class="field"><span>شماره تماس</span><input type="tel" placeholder="07xxxxxxxx" dir="ltr"></label><label class="field wide"><span>یادداشت</span><textarea placeholder="فقط توضیحاتی که کاربر وارد می‌کند"></textarea></label>`,'create');
  };
  document.querySelectorAll('.close-dialog').forEach(b=>b.onclick=()=>$('record-dialog').close());
  $('record-form').onsubmit=e=>{e.preventDefault();$('record-dialog').close();if(dialogMode==='create')notify('نمونه بررسی شد؛ هیچ اطلاعاتی ثبت نشده است.');};
  $('filters').onsubmit=e=>e.preventDefault();
  $('filters').oninput=()=>{page=1;render();}; $('filters').onchange=()=>{page=1;render();};
  $('filters').onreset=e=>{e.preventDefault();$('filters').querySelectorAll('input').forEach(input=>input.value='');$('filters').querySelectorAll('select').forEach(select=>select.selectedIndex=0);page=1;render();};
  $('more-filters').onclick=()=>{const open=$('advanced').hidden;$('advanced').hidden=!open;$('more-filters').setAttribute('aria-expanded',String(open));};
  document.querySelectorAll('[data-page]').forEach(b=>b.onclick=()=>switchPage(b.dataset.page));
  $('page-size').onchange=()=>{page=1;render();}; $('prev').onclick=()=>{page--;render();};$('next').onclick=()=>{page++;render();};
  $('clear-selection').onclick=()=>{selected.clear();render();};
  const selectedRows=()=>isTransfer()?transfers.filter(r=>selected.has(r.id)):views[current].rows.map((cells,i)=>({id:i+1,cells})).filter(r=>selected.has(r.id));
  $('copy').onclick=async()=>{const text=selectedRows().map(r=>isTransfer()?`نمبر مسلسل: ${r.id}\nنمبر حواله: ${r.ref}\nفرستنده: ${r.sender}\nگیرنده: ${r.receiver}\nمحل پرداخت: ${r.place}\nمبلغ: ${number(r.amount)} ${r.currency}\nتاریخ: ${r.displayDate}`:r.cells.join(' | ')).join('\n────────────\n');try{await navigator.clipboard.writeText(text);notify('جزئیات ساختگی انتخاب‌شده کپی شد.');}catch{openDialog('متن آماده کپی',`<label class="field wide"><span>متن ساختگی را انتخاب و کپی کنید</span><textarea readonly>${esc(text)}</textarea></label>`);}};
  $('export').onclick=()=>{const cells=selectedRows().map(r=>isTransfer()?[r.id,r.ref,r.sender,r.receiver,r.place,r.amount,r.currency,r.displayDate]:r.cells);const csv=cells.map(row=>row.map(v=>`"${String(v).replace(/"/g,'""')}"`).join(',')).join('\r\n');const url=URL.createObjectURL(new Blob(['\ufeff'+csv],{type:'text/csv;charset=utf-8;'}));const a=document.createElement('a');a.href=url;a.download='exchange-preview-synthetic.csv';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);notify('فایل CSV داده‌های ساختگی آماده شد؛ این خروجی نهایی اکسل سیستم نیست.');};
  $('execute').onclick=()=>{const eligible=selectedRows().filter(r=>r.status==='در انتظار اجرا');openDialog('پیش‌نمایش اجرای گروهی',`<p class="wide">از ${selected.size} حواله انتخاب‌شده، ${eligible.length} حواله در انتظار اجرا است. هیچ پرداخت واقعی انجام نمی‌شود.</p><label class="field wide"><span>حساب پرداخت نمونه</span><select><option>صندوق مرکزی — AFN</option><option>صندوق دالر — USD</option></select></label><p class="wide">در نسخه اصلی، تطابق ارز حساب و سایر اعتبارسنجی‌های حسابداری حفظ می‌شوند.</p>`);};
  $('bulk').onclick=()=>openDialog('آپلود گروهی — پیش‌نمایش',`<p class="wide">این نمونه فقط ترتیب و ظاهر مراحل را نشان می‌دهد؛ فایلی آپلود نمی‌شود.</p><ol class="wide"><li>انتخاب و بررسی فایل</li><li>ایجاد محل‌های پرداخت و تعیین مسئول</li><li>پیش‌نمایش حواله‌ها و مجموع هر ارز</li><li>تأیید و مشاهده پیشرفت ثبت</li></ol>`);
  $('refresh').onclick=()=>{render();notify('داده‌های ساختگی دوباره نمایش داده شد؛ این صفحه به دیتابیس متصل نیست.');};
  render();
})();
