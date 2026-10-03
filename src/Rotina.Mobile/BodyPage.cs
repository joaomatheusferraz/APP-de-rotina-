using Rotina.Application;
using Rotina.Domain;

namespace Rotina.Mobile;

public sealed class BodyPage : ContentPage
{
    private readonly GoalViewModel model;
    private readonly VerticalStackLayout content = new() { Spacing = 20, Padding = new Thickness(24, 20, 24, 32) };
    private readonly Picker period = new() { Title = "Período", ItemsSource = new[] { "7 dias", "30 dias", "90 dias" }, SelectedIndex = 1 };
    private readonly Picker metric = new() { Title = "Indicador" };
    private readonly int[] days = [7, 30, 90];
    public BodyPage(GoalViewModel model)
    {
        this.model = model;
        Title = "Corpo";
        var close = new ToolbarItem { Text = "Fechar" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(close);
        period.SelectedIndexChanged += (_, _) => Render();
        metric.SelectedIndexChanged += (_, _) => Render();
        Content = PageLayout.Body(PageLayout.Card(new Label { Text = "Indicador" }, metric, new Label { Text = "Período" }, period), content);
    }
    protected override async void OnAppearing() { base.OnAppearing(); await RefreshAsync(); }
    private async Task RefreshAsync()
    {
        await model.LoadAsync();
        var selected = metric.SelectedItem?.ToString();
        var items = new List<string> { "Peso (kg)" };
        items.AddRange(model.Measurements.Select(m => $"{m.Name} ({m.Unit})").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
        metric.ItemsSource = items;
        metric.SelectedItem = items.FirstOrDefault(x => x == selected) ?? items[0];
        Render();
    }
    private void Render()
    {
        content.Clear();
        content.Add(PageLayout.Heading("Evolução corporal"));
        content.Add(PageLayout.Muted("Peso e medidas têm históricos separados. Correções preservam o registro anterior."));
        if (model.Error is { } error) { content.Add(PageLayout.Muted(error)); return; }

        var count = days[Math.Max(0, period.SelectedIndex)];
        var today = DateOnly.FromDateTime(DateTime.Today);
        var isWeight = metric.SelectedIndex <= 0;
        var selected = metric.SelectedItem?.ToString() ?? "Peso (kg)";
        var measure = !isWeight ? model.Measurements.FirstOrDefault(m => selected == $"{m.Name} ({m.Unit})") : null;
        var points = isWeight ? ProgressAnalytics.Weights(model.Weights, today, count) :
            measure is null ? [] : ProgressAnalytics.Measurements(model.Measurements, measure.Name, measure.Unit, today, count);
        var unit = isWeight ? "kg" : measure?.Unit ?? "";
        var delta = points.Count > 1 ? points[^1].Value - points[0].Value : (double?)null;
        content.Add(PageLayout.Card(new Label { Text = selected, FontSize = 20, FontAttributes = FontAttributes.Bold },
            PageLayout.Muted(points.Count == 0 ? "Sem registros neste período" :
                $"Atual: {points[^1].Value:0.##} {unit}  •  mudança: {(delta is null ? "—" : $"{delta:+0.##;-0.##;0} {unit}")}"),
            TrendChart.Create(points, "Registre um valor para ver o gráfico.")));
        var addWeight = new Button { Text = "+ Registrar pesagem" };
        addWeight.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new WeightEditorPage(model, RefreshAsync)));
        var addMeasure = new Button { Text = "+ Registrar medida" };
        addMeasure.Clicked += async (_, _) => await Navigation.PushModalAsync(new NavigationPage(new MeasurementEditorPage(model, null, RefreshAsync)));
        content.Add(addWeight); content.Add(addMeasure);
        content.Add(new Label { Text = "Histórico", FontSize = 21, FontAttributes = FontAttributes.Bold });
        var rows = isWeight ? model.Weights.Select(w => (At: w.MeasuredAt, Name: "Peso", Value: w.Kilograms, Unit: "kg", Note: w.Note, Void: w.VoidedAt, Weight: (BodyWeightRecord?)w, Measurement: (BodyMeasurement?)null)) :
            model.Measurements.Where(m => selected == $"{m.Name} ({m.Unit})").Select(m => (At: m.MeasuredAt, Name: m.Name, Value: m.Value, Unit: m.Unit, Note: m.Note, Void: m.VoidedAt, Weight: (BodyWeightRecord?)null, Measurement: (BodyMeasurement?)m));
        var visible = rows.OrderByDescending(r => r.At).ToList();
        if (visible.Count == 0) content.Add(PageLayout.Muted("Nenhum registro ainda."));
        foreach (var row in visible)
        {
            var views = new List<View> { new Label { Text = $"{GoalUi.Number(row.Value)} {row.Unit}", FontSize = 18, FontAttributes = FontAttributes.Bold },
                PageLayout.Muted($"{row.At.ToLocalTime():dd/MM/yyyy HH:mm}" + (row.Void is null ? "" : " • corrigido ou desconsiderado")) };
            if (!string.IsNullOrWhiteSpace(row.Note)) views.Add(PageLayout.Muted(row.Note));
            if (row.Void is null)
            {
                var correct = new Button { Text = "Corrigir" };
                correct.Clicked += async (_, _) =>
                {
                    if (row.Weight is { } w) await Navigation.PushModalAsync(new NavigationPage(new WeightEditorPage(model, RefreshAsync, w)));
                    else if (row.Measurement is { } m) await Navigation.PushModalAsync(new NavigationPage(new MeasurementEditorPage(model, m, RefreshAsync)));
                };
                var disregard = new Button { Text = "Desconsiderar" };
                disregard.Clicked += async (_, _) =>
                {
                    if (!await DisplayAlert("Desconsiderar registro?", "Ele permanecerá no histórico, mas sairá dos gráficos.", "Desconsiderar", "Cancelar")) return;
                    if (row.Weight is { } w) await model.VoidWeightAsync(w);
                    else if (row.Measurement is { } m) await model.VoidMeasurementAsync(m);
                    await RefreshAsync();
                };
                views.Add(correct); views.Add(disregard);
            }
            content.Add(PageLayout.Card(views.ToArray()));
        }
    }
}

public sealed class MeasurementEditorPage : ContentPage
{
    public MeasurementEditorPage(GoalViewModel model, BodyMeasurement? existing, Func<Task> saved)
    {
        Title = existing is null ? "Nova medida" : "Corrigir medida";
        var name = new Entry { Placeholder = "Ex.: Cintura", Text = existing?.Name, MaxLength = 60 };
        var value = new Entry { Placeholder = "Valor", Text = existing is null ? "" : GoalUi.Number(existing.Value), Keyboard = Keyboard.Numeric };
        var unit = new Entry { Placeholder = "Unidade", Text = existing?.Unit ?? "cm", MaxLength = 20 };
        var local = existing?.MeasuredAt.ToLocalTime().DateTime ?? DateTime.Now;
        var date = new DatePicker { Format = "dd/MM/yyyy", Date = local.Date };
        var time = new TimePicker { Time = local.TimeOfDay };
        var note = new Entry { Placeholder = "Nota opcional", Text = existing?.Note, MaxLength = 1000 };
        var error = PageLayout.Muted("");
        var cancel = new ToolbarItem { Text = "Cancelar" };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(cancel);
        var save = new Button { Text = existing is null ? "Salvar medida" : "Salvar correção" };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var at = PlanningUi.LocalInstant(date.Date, time.Time);
                var amount = GoalUi.Parse(value.Text, "a medida");
                if (existing is null) await model.AddMeasurementAsync(name.Text ?? "", amount, unit.Text ?? "", at, note.Text);
                else await model.CorrectMeasurementAsync(existing, name.Text ?? "", amount, unit.Text ?? "", at, note.Text);
                await Navigation.PopModalAsync();
                await saved();
            }
            catch (Exception ex) { error.Text = ex.Message; save.IsEnabled = true; }
        };
        Content = PageLayout.Body(PageLayout.Heading(Title), PageLayout.Muted("Medidas são independentes das metas e podem ser corrigidas sem apagar o histórico."),
            PageLayout.Card(new Label { Text = "Nome" }, name, new Label { Text = "Valor" }, value,
                new Label { Text = "Unidade" }, unit, new Label { Text = "Data" }, date,
                new Label { Text = "Hora" }, time, new Label { Text = "Nota" }, note, error, save));
    }
}
