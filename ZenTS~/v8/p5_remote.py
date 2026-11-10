"""SSH/SFTP test-host helper; credentials are read from a caller-owned file, never copied."""
import argparse,re,shlex,sys
from pathlib import Path
import paramiko

def main():
    p=argparse.ArgumentParser();p.add_argument('--connection',type=Path,required=True);p.add_argument('--command');p.add_argument('--script',type=Path);p.add_argument('--upload',nargs=2);p.add_argument('--download',nargs=2);o=p.parse_args()
    content=o.connection.read_text(encoding='utf-8-sig')
    def field(label):return re.search(label+r'[:：]\s*(\S+)',content).group(1)
    client=paramiko.SSHClient();client.load_system_host_keys();client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(field('ip'),username=field('账户'),password=field('密码'),timeout=15)
    try:
        if o.upload or o.download:
            with client.open_sftp() as sftp:
                if o.upload:sftp.put(*o.upload)
                if o.download:Path(o.download[1]).parent.mkdir(parents=True,exist_ok=True);sftp.get(*o.download)
        if o.command or o.script:
            command=o.command or o.script.read_text(encoding='utf-8-sig')
            _,out,err=client.exec_command('bash -lc '+shlex.quote(command),timeout=600)
            for line in out:sys.stdout.buffer.write(line.encode('utf-8'));sys.stdout.buffer.flush()
            sys.stderr.buffer.write(err.read());return out.channel.recv_exit_status()
    finally:client.close()
    return 0
if __name__=='__main__':sys.exit(main())
