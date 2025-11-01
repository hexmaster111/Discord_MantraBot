sudo systemctl stop bot_mantrachecker.service
git pull
sudo systemctl start bot_mantrachecker.service
journalctl -u bot_mantrachecker.service -b