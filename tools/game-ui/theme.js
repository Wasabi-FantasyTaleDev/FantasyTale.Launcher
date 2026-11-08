/** Apply the product's release-date boundary while retaining the older theme choice. */
export function themeForClientDate(clientDate,previousTheme='v2'){
  if(!['v1','v2','v3'].includes(previousTheme))throw Error(`Unknown UI theme: ${previousTheme}`);
  if(clientDate===undefined||clientDate==='')return previousTheme;
  if(typeof clientDate!=='string'||!/^\d{4}-\d{2}-\d{2}$/.test(clientDate))throw Error('Client date must use YYYY-MM-DD.');
  const parsed=new Date(clientDate+'T00:00:00Z');
  if(!Number.isFinite(parsed.getTime())||parsed.toISOString().slice(0,10)!==clientDate)throw Error(`Invalid client date: ${clientDate}`);
  return clientDate>='2017-02-07'?'v3':previousTheme;
}
