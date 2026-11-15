from threading import Thread
from pythonosc.dispatcher import Dispatcher
from pythonosc.osc_server import BlockingOSCUDPServer
from pythonosc.udp_client import SimpleUDPClient
import time

target = "192.168.137.95" #headset
reply_ip = "192.168.137.1" #Workstation
#target = "127.0.0.1"
#reply_ip = "127.0.0.1"
show_poses = False

def received(address, *args):
    if show_poses or address not in ("/xr/user/pose", "/xr/hands/pose"):
        print(address, args)

dispatcher = Dispatcher()
dispatcher.set_default_handler(received)
server = BlockingOSCUDPServer(("0.0.0.0", 6012), dispatcher)
Thread(target=server.serve_forever, daemon=True).start()
send = SimpleUDPClient(target, 6011).send_message
#send("/connect", [reply_ip, 6012])

#time.sleep(0.2)

for _ in range(3):
    send("/connect", [reply_ip, 6012])
    time.sleep(1)
time.sleep(3)