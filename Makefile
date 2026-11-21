NAME    := opencode-phone-proxy
VERSION := $(shell cat VERSION)
PREFIX  ?= $(HOME)/.local
DIST    := dist
FILES   := opencode-phone-proxy.sh README.md CHANGELOG.md ROADMAP.md VERSION Makefile docs tests

.PHONY: all check test package install uninstall release clean

all: test package

check:
	@bash -n opencode-phone-proxy.sh
	@bash -n tests/smoke.sh
	@if command -v shellcheck >/dev/null 2>&1; then \
		shellcheck -S error opencode-phone-proxy.sh tests/smoke.sh; \
	else \
		echo "shellcheck not installed; skipping"; \
	fi
	@v="$$(cat VERSION)"; \
		grep -Fq "VERSION=\"$$v\"" opencode-phone-proxy.sh || { \
			echo "VERSION file and script VERSION do not match"; exit 1; \
		}

test: check
	@bash tests/smoke.sh

package: test
	@rm -rf "$(DIST)/$(NAME)-v$(VERSION)"
	@mkdir -p "$(DIST)/$(NAME)-v$(VERSION)"
	@cp -R $(FILES) "$(DIST)/$(NAME)-v$(VERSION)/"
	@tar -C "$(DIST)" -czf "$(DIST)/$(NAME)-v$(VERSION).tar.gz" "$(NAME)-v$(VERSION)"
	@rm -rf "$(DIST)/$(NAME)-v$(VERSION)"
	@cd "$(DIST)" && sha256sum "$(NAME)-v$(VERSION).tar.gz" > "$(NAME)-v$(VERSION).tar.gz.sha256"
	@echo "Built $(DIST)/$(NAME)-v$(VERSION).tar.gz"

install: check
	@install -d "$(DESTDIR)$(PREFIX)/bin"
	@install -m 0755 opencode-phone-proxy.sh "$(DESTDIR)$(PREFIX)/bin/$(NAME)"
	@install -d "$(DESTDIR)$(PREFIX)/share/doc/$(NAME)"
	@install -m 0644 README.md CHANGELOG.md "$(DESTDIR)$(PREFIX)/share/doc/$(NAME)/"
	@echo "Installed $(PREFIX)/bin/$(NAME)"

uninstall:
	@rm -f "$(DESTDIR)$(PREFIX)/bin/$(NAME)"
	@rm -rf "$(DESTDIR)$(PREFIX)/share/doc/$(NAME)"

release: package
	@echo "Artifact ready: $(DIST)/$(NAME)-v$(VERSION).tar.gz"
	@echo "Tag with: git tag -a v$(VERSION) -m 'Release v$(VERSION)' && git push origin v$(VERSION)"

clean:
	@rm -rf "$(DIST)"
