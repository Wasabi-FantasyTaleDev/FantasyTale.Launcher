"""Execute a verified modern input branch with modeled native eligibility results.

This checks original x86 control flow and manifest bytes. External actor methods
are modeled, so it does not establish live gameplay acceptance.
"""
from pathlib import Path
import argparse
import json
import re
import struct
import tomllib

import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn import x86_const as reg


def verify(image, manifest, output):
    """Verify TW454's independent timer bypass without bypassing run eligibility."""
    pe=pefile.PE(image)
    base=pe.OPTIONAL_HEADER.ImageBase
    original=pe.get_memory_mapped_image()
    definition=tomllib.loads(Path(manifest).read_text(encoding='utf-8'))
    row=next(row for row in definition['patches'] if row['builds']==['tw-454'])
    pattern=b''.join(b'.' if part in ('?','??') else re.escape(bytes.fromhex(part)) for part in row['locate']['pattern'].split())
    hits=[section.VirtualAddress+match.start() for section in pe.sections if section.Characteristics&0x20000000
          for match in re.finditer(pattern,section.get_data(),re.S)]
    if len(hits)!=1: raise ValueError('The executable signature must be unique')
    site=hits[0]+row['locate'].get('offset',0)
    expected=bytes.fromhex(row['expect'])
    replacement=bytes.fromhex(row['write'])
    if original[site:site+len(expected)]!=expected or len(expected)!=len(replacement):
        raise ValueError('Original branch bytes differ')
    if base+site!=0x801AA4 or replacement!=b'\xeb\x26':
        raise ValueError('Unexpected branch or native destination')
    actor,timer,vtable,stub,stack=0x10000000,0x10008000,0x1000C000,0x1000D000,0x11000000
    cases=[
        ('first-press',101,True,True,0,0,True,False,0,1),
        ('active-double-press',101,True,True,300,950,True,False,1,1),
        ('expired-double-press',101,True,True,300,100,True,False,0,1),
        ('not-key-down',101,False,True,0,0,True,False,0,0),
        ('denied-native-eligibility',101,True,True,300,950,False,False,0,0),
        ('denied-first-press',101,True,True,0,0,False,False,0,0),
        ('blocked-virtual-check',101,True,True,0,0,True,True,0,0),
        ('other-running-state',103,True,True,0,0,True,False,0,1),
        ('already-running-held',104,True,True,0,0,True,False,0,0),
        ('already-running-release',104,False,False,0,0,True,False,0,0),
    ]
    results=[]
    for patched in (False,True):
        payload=bytearray(original)
        if patched: payload[site:site+len(replacement)]=replacement
        for name,state,pressed,held,duration,start,allowed,blocked,before,after in cases:
            uc=Uc(UC_ARCH_X86,UC_MODE_32)
            uc.mem_map(base,(len(payload)+4095)&~4095)
            uc.mem_write(base,bytes(payload))
            uc.mem_map(actor,0x20000)
            uc.mem_map(stack,0x10000)
            def put(address,value): uc.mem_write(address,struct.pack('<I',value))
            put(actor,vtable)
            put(actor+0x53DC,timer)
            put(timer+4,0x1316860)
            put(timer+12,duration)
            put(timer+16,start)
            put(0x16405E8,1000)
            put(vtable+948,stub)
            uc.reg_write(reg.UC_X86_REG_EDI,actor)
            uc.reg_write(reg.UC_X86_REG_EBX,int(held)|(int(pressed)<<8))
            uc.reg_write(reg.UC_X86_REG_EBP,stack+0x8000)
            uc.reg_write(reg.UC_X86_REG_ESP,stack+0x7000)
            calls={'run':0,'arm':0,'release':0,'eligibility':0,'virtual':0}
            def finish(value,arguments=0):
                esp=uc.reg_read(reg.UC_X86_REG_ESP)
                target=struct.unpack('<I',uc.mem_read(esp,4))[0]
                uc.reg_write(reg.UC_X86_REG_EAX,value)
                uc.reg_write(reg.UC_X86_REG_ESP,esp+4+arguments)
                uc.reg_write(reg.UC_X86_REG_EIP,target)
            def trace(machine,address,size,user):
                if address==0x801B03: machine.emu_stop()
                elif address==0x792E30: finish(state)
                elif address==stub: calls['virtual']+=1; finish(int(blocked))
                elif address==0x80D1C0: calls['eligibility']+=1; finish(int(allowed))
                elif address==0x80BE70: calls['run']+=1; finish(1)
                elif address==0x80BB10: calls['release']+=1; finish(1)
                elif address==0xEDB430: calls['arm']+=1; finish(0,4)
            uc.hook_add(UC_HOOK_CODE,trace)
            uc.emu_start(0x801A6E,0,count=500)
            if uc.reg_read(reg.UC_X86_REG_EIP)!=0x801B03: raise ValueError('Input path did not finish')
            expected_runs=after if patched else before
            if calls['run']!=expected_runs: raise ValueError(f'{name}: unexpected run transition {calls}')
            if calls['run'] and (calls['eligibility']!=1 or calls['virtual']!=1):
                raise ValueError('Native running restrictions were skipped')
            if patched and calls['arm']: raise ValueError('Patched forward input still arms the double-press timer')
            if calls['release']!=int(state==104 and not held): raise ValueError('Running release behavior changed')
            results.append({'case':name,'patched':patched,**calls})
    Path(output).write_text(json.dumps({'build':'tw-454','site':hex(base+site),'unique_signature':True,'cases':results},indent=2),encoding='utf-8')
    print(f'PASS tw-454: unique original signature and {len(results)} original/patched x86 input cases')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--image',required=True,type=Path)
    parser.add_argument('--manifest',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    args=parser.parse_args()
    verify(args.image,args.manifest,args.output)
