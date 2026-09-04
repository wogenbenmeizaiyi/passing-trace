package com.passingtrace.passingtrace_mobile

import android.app.Activity
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.os.Bundle
import android.view.Gravity
import android.view.ViewGroup
import android.widget.Button
import android.widget.FrameLayout
import android.widget.ImageButton
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import com.amap.api.maps.AMap
import com.amap.api.maps.CameraUpdateFactory
import com.amap.api.maps.MapView
import com.amap.api.maps.MapsInitializer
import com.amap.api.maps.model.CameraPosition
import com.amap.api.maps.model.LatLng

class MapPickerActivity : Activity(), AMap.OnCameraChangeListener {
    companion object {
        const val EXTRA_LATITUDE = "latitude"
        const val EXTRA_LONGITUDE = "longitude"
    }

    private lateinit var mapView: MapView
    private lateinit var map: AMap
    private lateinit var hint: TextView
    private lateinit var confirm: Button
    private lateinit var initialPoint: LatLng
    private lateinit var selectedPoint: LatLng

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        MapsInitializer.updatePrivacyShow(applicationContext, true, true)
        MapsInitializer.updatePrivacyAgree(applicationContext, true)

        initialPoint = LatLng(
            intent.getDoubleExtra(EXTRA_LATITUDE, 39.908823),
            intent.getDoubleExtra(EXTRA_LONGITUDE, 116.397470)
        )
        selectedPoint = initialPoint
        window.statusBarColor = PAPER
        window.navigationBarColor = PAPER
        val content = buildContent(savedInstanceState)
        ViewCompat.setOnApplyWindowInsetsListener(content) { view, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            view.setPadding(0, bars.top, 0, bars.bottom)
            insets
        }
        setContentView(content)

        map = mapView.map
        map.setOnCameraChangeListener(this)
        map.uiSettings.apply {
            isZoomControlsEnabled = false
            isMyLocationButtonEnabled = false
            isRotateGesturesEnabled = false
            isTiltGesturesEnabled = false
            isScaleControlsEnabled = true
        }
        map.moveCamera(CameraUpdateFactory.newLatLngZoom(initialPoint, 17f))
    }

    private fun buildContent(savedInstanceState: Bundle?): LinearLayout {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(PAPER)
        }
        root.addView(buildToolbar(), LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            dp(58)
        ))

        val mapFrame = FrameLayout(this)
        mapView = MapView(this).also { it.onCreate(savedInstanceState) }
        mapFrame.addView(mapView, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT
        ))

        val pin = ImageView(this).apply {
            setImageResource(R.drawable.ic_map_pin)
            contentDescription = "地图中心选点"
            elevation = dp(6).toFloat()
        }
        mapFrame.addView(pin, FrameLayout.LayoutParams(dp(48), dp(48), Gravity.CENTER).apply {
            bottomMargin = dp(24)
        })

        hint = TextView(this).apply {
            text = "拖动地图，选择要记录的位置"
            textSize = 15f
            setTextColor(INK)
            setPadding(dp(16), dp(12), dp(16), dp(12))
            background = panelBackground()
            elevation = dp(4).toFloat()
            gravity = Gravity.CENTER
        }
        mapFrame.addView(hint, FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.WRAP_CONTENT,
            Gravity.TOP
        ).apply { setMargins(dp(20), dp(18), dp(20), 0) })

        val recenter = ImageButton(this).apply {
            setImageResource(android.R.drawable.ic_menu_compass)
            setColorFilter(VERMILION)
            background = panelBackground()
            contentDescription = "回到当前位置"
            elevation = dp(4).toFloat()
            setOnClickListener {
                map.animateCamera(CameraUpdateFactory.newLatLngZoom(initialPoint, 17f))
            }
        }
        mapFrame.addView(recenter, FrameLayout.LayoutParams(dp(52), dp(52), Gravity.END or Gravity.BOTTOM).apply {
            setMargins(0, 0, dp(20), dp(28))
        })

        root.addView(mapFrame, LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            0,
            1f
        ))
        return root
    }

    private fun buildToolbar(): LinearLayout = LinearLayout(this).apply {
        gravity = Gravity.CENTER_VERTICAL
        setPadding(dp(8), 0, dp(8), 0)
        addView(Button(this@MapPickerActivity).apply {
            text = "返回"
            textSize = 15f
            setTextColor(INK)
            setBackgroundColor(Color.TRANSPARENT)
            setOnClickListener { finish() }
        }, LinearLayout.LayoutParams(dp(76), ViewGroup.LayoutParams.MATCH_PARENT))
        addView(TextView(this@MapPickerActivity).apply {
            text = "选择地点"
            textSize = 19f
            typeface = Typeface.create("serif", Typeface.BOLD)
            setTextColor(INK)
            gravity = Gravity.CENTER
        }, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f))
        confirm = Button(this@MapPickerActivity).apply {
            text = "确定"
            textSize = 15f
            setTextColor(VERMILION)
            setBackgroundColor(Color.TRANSPARENT)
            setOnClickListener {
                setResult(RESULT_OK, Intent().apply {
                    putExtra(EXTRA_LATITUDE, selectedPoint.latitude)
                    putExtra(EXTRA_LONGITUDE, selectedPoint.longitude)
                })
                finish()
            }
        }
        addView(confirm, LinearLayout.LayoutParams(dp(76), ViewGroup.LayoutParams.MATCH_PARENT))
    }

    override fun onCameraChange(position: CameraPosition) {
        hint.text = "移动中…"
        confirm.isEnabled = false
    }

    override fun onCameraChangeFinish(position: CameraPosition) {
        selectedPoint = position.target
        hint.text = "确定后可选择这个位置附近的地点"
        confirm.isEnabled = true
    }

    override fun onResume() {
        super.onResume()
        mapView.onResume()
    }

    override fun onPause() {
        mapView.onPause()
        super.onPause()
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        mapView.onSaveInstanceState(outState)
    }

    override fun onDestroy() {
        mapView.onDestroy()
        super.onDestroy()
    }

    private fun panelBackground() = GradientDrawable().apply {
        setColor(Color.argb(242, 255, 252, 246))
        setStroke(dp(1), Color.argb(45, 53, 45, 42))
        cornerRadius = dp(8).toFloat()
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private val PAPER = Color.rgb(247, 242, 230)
    private val INK = Color.rgb(53, 45, 42)
    private val VERMILION = Color.rgb(220, 72, 59)
}
