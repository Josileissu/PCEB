/* PCEB — comportamento progressivo e sem dependências */
(function () {
  "use strict";

  var header = document.querySelector(".site-header");
  var toggle = document.querySelector(".nav-toggle");
  var nav = document.querySelector(".nav");

  if (toggle && nav) {
    var setMenu = function (open) {
      toggle.setAttribute("aria-expanded", open ? "true" : "false");
      nav.classList.toggle("is-open", open);
    };

    toggle.addEventListener("click", function () {
      setMenu(toggle.getAttribute("aria-expanded") !== "true");
    });

    nav.querySelectorAll("a").forEach(function (link) {
      link.addEventListener("click", function () { setMenu(false); });
    });

    document.addEventListener("keydown", function (event) {
      if (event.key === "Escape" && nav.classList.contains("is-open")) {
        setMenu(false);
        toggle.focus();
      }
    });

    window.addEventListener("resize", function () {
      if (window.innerWidth > 900) setMenu(false);
    });
  }

  if (header) {
    var onScroll = function () {
      header.classList.toggle("is-scrolled", window.scrollY > 8);
    };
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
  }

  var reveals = document.querySelectorAll(".reveal");
  if (reveals.length) {
    if (!("IntersectionObserver" in window) ||
        window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      reveals.forEach(function (el) { el.classList.add("is-in"); });
    } else {
      var observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
          if (entry.isIntersecting) {
            entry.target.classList.add("is-in");
            observer.unobserve(entry.target);
          }
        });
      }, { rootMargin: "0px 0px -8% 0px", threshold: 0.08 });

      reveals.forEach(function (el, index) {
        el.style.setProperty("--reveal-delay", (index % 4) * 0.06 + "s");
        observer.observe(el);
      });
    }
  }

  // Versão e instalador da Release mais recente do GitHub. Sem rede/API, os links
  // continuam apontando para releases/latest e os textos ficam como estão no HTML.
  var alvos = document.querySelectorAll("[data-release]");
  if (alvos.length && window.fetch) {
    fetch("https://api.github.com/repos/Josileissu/PCEB/releases/latest", {
      headers: { "Accept": "application/vnd.github+json" }
    }).then(function (r) { return r.ok ? r.json() : null; }).then(function (rel) {
      if (!rel || !rel.tag_name) return;
      var versao = rel.tag_name.replace(/^v/i, "");
      var exe = (rel.assets || []).filter(function (a) { return /\.exe$/i.test(a.name); })[0];
      var data = rel.published_at ? new Date(rel.published_at).toLocaleDateString("pt-BR") : "";
      alvos.forEach(function (el) {
        var k = el.getAttribute("data-release");
        if (k === "versao") el.textContent = versao;
        else if (k === "data" && data) el.textContent = data;
        else if (k === "arquivo" && exe) el.textContent = exe.name;
        else if (k === "download" && exe) el.setAttribute("href", exe.browser_download_url);
      });
    }).catch(function () {});
  }

  // Instalador do Revit: release própria (tag revit-v…), que não é a "mais recente" do repositório — procura na lista a
  // última release com o DEB_Revit_Setup_*.exe. Sem a API, o botão abre a página das releases.
  var alvosRevit = document.querySelectorAll("[data-revit]");
  if (alvosRevit.length && window.fetch) {
    fetch("https://api.github.com/repos/Josileissu/PCEB/releases?per_page=30", {
      headers: { "Accept": "application/vnd.github+json" }
    }).then(function (r) { return r.ok ? r.json() : null; }).then(function (lista) {
      if (!lista || !lista.length) return;
      var achado = null, exe = null;
      for (var i = 0; i < lista.length && !achado; i++) {
        if (lista[i].draft || lista[i].prerelease) continue;
        var a = (lista[i].assets || []).filter(function (x) { return /^DEB_Revit_Setup_.*\.exe$/i.test(x.name); })[0];
        if (a) { achado = lista[i]; exe = a; }
      }
      if (!achado) return;
      var versao = achado.tag_name.replace(/^revit-v/i, "").replace(/^v/i, "");
      alvosRevit.forEach(function (el) {
        var k = el.getAttribute("data-revit");
        if (k === "versao") el.textContent = versao;
        else if (k === "arquivo") el.textContent = exe.name;
        else if (k === "download") el.setAttribute("href", exe.browser_download_url);
      });
    }).catch(function () {});
  }

  // Doação: QR code do Pix (assets/js/qrcode.js, MIT) e botão de copiar.
  document.querySelectorAll("[data-pix-qr]").forEach(function (el) {
    var fonte = document.querySelector(el.getAttribute("data-pix-qr"));
    if (!fonte || typeof qrcode !== "function") return;
    var qr = qrcode(0, "M");
    qr.addData(fonte.value.trim(), "Byte");
    qr.make();
    el.innerHTML = qr.createSvgTag({ cellSize: 4, margin: 4, scalable: true });
  });
  document.querySelectorAll("[data-copiar]").forEach(function (bt) {
    var fonte = document.querySelector(bt.getAttribute("data-copiar"));
    var aviso = document.querySelector("[data-copiar-aviso]");
    if (!fonte) return;
    bt.addEventListener("click", function () {
      var ok = function () { if (aviso) aviso.textContent = "Código copiado. Cole no app do banco em Pix copia e cola."; };
      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(fonte.value.trim()).then(ok, function () { fonte.select(); document.execCommand("copy"); ok(); });
      } else { fonte.select(); document.execCommand("copy"); ok(); }
    });
  });

  document.querySelectorAll("[data-year]").forEach(function (el) {
    el.textContent = String(new Date().getFullYear());
  });
})();
