"""Execute commands or transfer files using a caller-owned Bitvise profile."""
import argparse,subprocess,sys
from pathlib import Path
BASE=Path('C:/Program Files (x86)/Bitvise SSH Client')
def main():
    p=argparse.ArgumentParser();p.add_argument('--profile',required=True);p.add_argument('--command');p.add_argument('--script',type=Path);p.add_argument('--upload',nargs=2);p.add_argument('--download',nargs=2);p.add_argument('--timeout',type=int,default=600);a=p.parse_args()
    if a.upload or a.download:
        op='put' if a.upload else 'get';src,dst=a.upload or a.download
        command=op+' "'+src.replace(chr(92),'/')+'" "'+dst.replace(chr(92),'/')+'" -o'
        args=[str(BASE/'sftpc.exe'),'-profile='+a.profile,'-unat=y','-progress=none','-cmd='+command]
    else:
        command=a.command or a.script.read_text(encoding='utf-8-sig')
        args=[str(BASE/'sexec.exe'),'-profile='+a.profile,'-unat=y','-exitZero','-cmd='+command]
    run=subprocess.run(args,capture_output=True,timeout=a.timeout)
    if a.upload or a.download:
        if run.returncode == 0:print('Transfer complete:',src,'->',dst);return 0
    output=run.stdout.decode('utf-8','replace')
    # Suppress the client's banner/update advertisement, retain remote output.
    marker=output.find('Connecting to ')
    if marker>=0:output=output[marker:]
    elif len(output)>2500 and output.startswith('Bitvise'):output=output[-2500:]
    print(output,end='');sys.stderr.buffer.write(run.stderr)
    return run.returncode
if __name__=='__main__':sys.exit(main())
