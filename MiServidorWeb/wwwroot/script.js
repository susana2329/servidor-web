document.addEventListener("DOMContentLoaded", function () {
    const resultado = document.getElementById("resultado");

    document.querySelectorAll("[data-ruta]").forEach(function (boton) {
        boton.addEventListener("click", async function () {
            const ruta = boton.dataset.ruta;
            resultado.className = "";
            resultado.textContent = "Pidiendo " + ruta + "...";

            try {
                const inicio = performance.now();
                const respuesta = await fetch(ruta, { cache: "no-store" });
                const ms = Math.round(performance.now() - inicio);

                resultado.textContent = "GET " + ruta + " devolvió " + respuesta.status +
                    (respuesta.statusText ? " " + respuesta.statusText : "") + " en " + ms + " ms.";
                resultado.className = respuesta.ok ? "ok" : "error";
            } catch (error) {
                resultado.textContent = "No se pudo conectar con el servidor.";
                resultado.className = "error";
            }
        });
    });
});