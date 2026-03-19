SERVICE_NAME := bird-server
SERVICE_FILE := bird-server.service
SYSTEMD_DIR  := /etc/systemd/system
ENV_FILE     ?= $(HOME)/.config/crypton/.env

USER_NAME    := $(shell whoami)
BIRD_PATH    := $(shell which bird)
NPM_BIN_DIR  := $(shell dirname $(BIRD_PATH))
NODE_PATH    := $(shell which node)
WORK_DIR     := $(CURDIR)

# Resolve bird's library path for token extraction
BIRD_LIB     := $(shell dirname $(BIRD_PATH))/../lib/node_modules/@steipete/bird/dist/lib/cookies.js

.PHONY: install uninstall start stop restart status extract-tokens

extract-tokens:
	@echo "Extracting auth tokens from browser via bird library..."
	@node --input-type=module -e " \
	  import { resolveCredentials } from '$(BIRD_LIB)'; \
	  const { cookies } = await resolveCredentials({}); \
	  if (!cookies.authToken || !cookies.ct0) { \
	    console.error('Error: could not resolve Twitter credentials from browser.'); \
	    process.exit(1); \
	  } \
	  import { readFileSync, writeFileSync, mkdirSync } from 'node:fs'; \
	  import { dirname } from 'node:path'; \
	  const envFile = '$(ENV_FILE)'; \
	  mkdirSync(dirname(envFile), { recursive: true }); \
	  let content = ''; \
	  try { content = readFileSync(envFile, 'utf8'); } catch {} \
	  const update = (key, val) => { \
	    const re = new RegExp('^' + key + '=.*$$', 'm'); \
	    if (re.test(content)) { content = content.replace(re, key + '=' + val); } \
	    else { content = content.trimEnd() + '\n' + key + '=' + val + '\n'; } \
	  }; \
	  update('BIRD_AUTH_TOKEN', cookies.authToken); \
	  update('BIRD_CT0', cookies.ct0); \
	  writeFileSync(envFile, content); \
	  console.log('Wrote BIRD_AUTH_TOKEN and BIRD_CT0 to ' + envFile); \
	"

install:
	@test -n "$(BIRD_PATH)" || { echo "Error: bird not found on PATH"; exit 1; }
	@test -f "$(ENV_FILE)" || { echo "Error: $(ENV_FILE) not found. Run 'make extract-tokens' first."; exit 1; }
	$(eval AUTH_TOKEN := $(shell grep '^BIRD_AUTH_TOKEN=' "$(ENV_FILE)" | cut -d= -f2-))
	$(eval CT0 := $(shell grep '^BIRD_CT0=' "$(ENV_FILE)" | cut -d= -f2-))
	@test -n "$(AUTH_TOKEN)" || { echo "Error: BIRD_AUTH_TOKEN not found in $(ENV_FILE). Run 'make extract-tokens' first."; exit 1; }
	@test -n "$(CT0)" || { echo "Error: BIRD_CT0 not found in $(ENV_FILE). Run 'make extract-tokens' first."; exit 1; }
	@echo '[Unit]' > /tmp/$(SERVICE_FILE)
	@echo 'Description=Crypton Bird Server — HTTP wrapper for bird CLI' >> /tmp/$(SERVICE_FILE)
	@echo 'After=network.target' >> /tmp/$(SERVICE_FILE)
	@echo '' >> /tmp/$(SERVICE_FILE)
	@echo '[Service]' >> /tmp/$(SERVICE_FILE)
	@echo 'Type=simple' >> /tmp/$(SERVICE_FILE)
	@echo 'User=$(USER_NAME)' >> /tmp/$(SERVICE_FILE)
	@echo 'Environment="PATH=$(NPM_BIN_DIR):/usr/local/bin:/usr/bin:/bin"' >> /tmp/$(SERVICE_FILE)
	@echo 'Environment="BIRD_HOST=0.0.0.0"' >> /tmp/$(SERVICE_FILE)
	@echo 'Environment="BIRD_PORT=11435"' >> /tmp/$(SERVICE_FILE)
	@echo 'Environment="BIRD_AUTH_TOKEN=$(AUTH_TOKEN)"' >> /tmp/$(SERVICE_FILE)
	@echo 'Environment="BIRD_CT0=$(CT0)"' >> /tmp/$(SERVICE_FILE)
	@echo 'WorkingDirectory=$(WORK_DIR)' >> /tmp/$(SERVICE_FILE)
	@echo 'ExecStart=$(NODE_PATH) server.js' >> /tmp/$(SERVICE_FILE)
	@echo 'Restart=on-failure' >> /tmp/$(SERVICE_FILE)
	@echo 'RestartSec=5' >> /tmp/$(SERVICE_FILE)
	@echo '' >> /tmp/$(SERVICE_FILE)
	@echo '[Install]' >> /tmp/$(SERVICE_FILE)
	@echo 'WantedBy=multi-user.target' >> /tmp/$(SERVICE_FILE)
	sudo mv /tmp/$(SERVICE_FILE) $(SYSTEMD_DIR)/$(SERVICE_FILE)
	sudo systemctl daemon-reload
	sudo systemctl enable --now $(SERVICE_NAME)
	sudo ufw allow from 172.16.0.0/12 to any port 11435 proto tcp comment "Allow Docker containers to reach Bird Server"
	@echo "$(SERVICE_NAME) installed and started."

uninstall:
	-sudo systemctl disable --now $(SERVICE_NAME)
	-sudo rm -f $(SYSTEMD_DIR)/$(SERVICE_FILE)
	sudo systemctl daemon-reload
	-sudo ufw delete allow from 172.16.0.0/12 to any port 11435 proto tcp
	@echo "$(SERVICE_NAME) uninstalled."

start:
	sudo systemctl start $(SERVICE_NAME)
	@echo "$(SERVICE_NAME) started."

stop:
	sudo systemctl stop $(SERVICE_NAME)
	@echo "$(SERVICE_NAME) stopped."

restart:
	sudo systemctl restart $(SERVICE_NAME)
	@echo "$(SERVICE_NAME) restarted."

status:
	@systemctl status $(SERVICE_NAME) --no-pager || true
