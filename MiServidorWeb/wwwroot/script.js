document.addEventListener("DOMContentLoaded", function () {
    const resultado = document.getElementById("resultado");

    let temporizador = null;

    document.querySelectorAll("[data-ruta]").forEach(function (boton) {
        boton.addEventListener("click", async function () {
            const ruta = boton.dataset.ruta;
            clearTimeout(temporizador);   // cancela una pestaña pendiente de un clic anterior
            resultado.className = "";
            resultado.textContent = "Pidiendo " + ruta + "...";

            try {
                const inicio = performance.now();
                const respuesta = await fetch(ruta, { cache: "no-store" });
                const ms = Math.round(performance.now() - inicio);

                // "OK" se deja como viene; el resto ("Not Found") se muestra en minúscula.
                const estado = respuesta.statusText === "OK" ? "OK" : respuesta.statusText.toLowerCase();
                resultado.textContent = "GET " + ruta + " devolvió " + respuesta.status +
                    (estado ? " " + estado : "") + " en " + ms + " ms.";
                resultado.className = respuesta.ok ? "ok" : "error";

                // Punto 5: pasado ese tiempo, se abre la página de error en una pestaña nueva.
                if (boton.dataset.abrirTras !== undefined) {
                    temporizador = setTimeout(function () {
                        const pestana = window.open(ruta, "_blank");
                        if (!pestana) resultado.textContent += " El navegador bloqueó la pestaña nueva.";
                    }, Number(boton.dataset.abrirTras));
                }
            } catch (error) {
                resultado.textContent = "No se pudo conectar con el servidor.";
                resultado.className = "error";
            }
        });
    });

    // ----- Aviso emergente al enviar los formularios GET y POST -----
    const aviso = document.getElementById("aviso");

    function mostrarAviso(ok, titulo, detalle) {
        if (typeof aviso.showModal !== "function") {   // navegador viejo: alert común
            alert(titulo + "\n\n" + detalle);
            return;
        }
        aviso.className = ok ? "ok" : "error";
        document.getElementById("aviso-titulo").textContent = titulo;
        document.getElementById("aviso-detalle").textContent = detalle;
        aviso.showModal();
    }

    // Envía el formulario con fetch (sin cambiar de página). Al servidor le llega el mismo pedido
    // HTTP que con el envío normal: GET con los datos en la URL, o POST con los datos en el cuerpo.
    function conectarFormulario(form, metodo) {
        form.addEventListener("submit", async function (evento) {
            evento.preventDefault();

            const campos = new FormData(form);
            const datos = new URLSearchParams(campos);   // clave=valor&clave=valor
            const legibles = Array.from(campos, function (par) { return par[0] + ": " + par[1]; }).join("\n");
            const url = metodo === "GET" ? "/?" + datos.toString() : "/";
            const opciones = { cache: "no-store" };
            if (metodo === "POST") {
                opciones.method = "POST";
                opciones.body = datos;   // el navegador agrega Content-Type y Content-Length
            }

            const boton = form.querySelector("button[type=submit]");
            boton.disabled = true;
            try {
                const respuesta = await fetch(url, opciones);
                const estado = respuesta.status + (respuesta.statusText ? " " + respuesta.statusText : "");
                if (respuesta.ok) {
                    mostrarAviso(true, "¡Realizado con éxito!",
                        "Método: " + metodo + "\n" + legibles + "\nRespuesta del servidor: " + estado);
                    form.reset();
                } else {
                    mostrarAviso(false, "El servidor respondió con un error",
                        "Método: " + metodo + "\nRespuesta del servidor: " + estado);
                }
            } catch (error) {
                mostrarAviso(false, "No se pudo conectar con el servidor",
                    "Revisá que el servidor esté en ejecución.");
            } finally {
                boton.disabled = false;
            }
        });
    }

    conectarFormulario(document.getElementById("form-get"), "GET");
    conectarFormulario(document.getElementById("form-post"), "POST");
});